using System.Net;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// 旧版「一人公司」API：配置、聊天代理、状态查询、员工管理、项目看板。
/// 保留完整的向下兼容，不做逻辑修改，仅字段引用从 _config 改为 AppContext.Config。
/// </summary>
public static class LegacyCompanyApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var config = AppContext.Config;
        var httpClient = AppContext.HttpClient;

        // Config
        if (path == "/api/config" && req.HttpMethod == "GET")
        {
            string targetUrl = string.IsNullOrEmpty(config.MasterNodeUrl) ? "http://127.0.0.1:5050" : config.MasterNodeUrl;
            try
            {
                var proxyReq = new HttpRequestMessage(HttpMethod.Get, targetUrl.TrimEnd('/') + "/api/config");
                using var proxyRes = await httpClient.SendAsync(proxyReq);
                if (proxyRes.IsSuccessStatusCode)
                {
                    var jsonStr = await proxyRes.Content.ReadAsStringAsync();
                    var masterCfg = JsonSerializer.Deserialize(jsonStr, AppJsonContext.Default.DictionaryStringJsonElement);
                    if (masterCfg != null && masterCfg.TryGetValue("PeerNodes", out var peerNodesEl))
                    {
                        var freshNodes = JsonSerializer.Deserialize(peerNodesEl.GetRawText(), AppJsonContext.Default.DictionaryStringNodeInfo);
                        if (freshNodes != null) config.PeerNodes = freshNodes;
                    }
                }
            }
            catch { /* 底层挂掉则使用本地缓存 */ }

            res.ContentType = "application/json; charset=utf-8";
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer);
            return true;
        }

        if (path == "/api/config" && req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream);
            string body = await reader.ReadToEndAsync();
            var newConfig = JsonSerializer.Deserialize<AppConfig>(body, AppJsonContext.Default.AppConfig);
            if (newConfig != null)
            {
                AppContext.Config = newConfig;
                File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(AppContext.Config, AppJsonContext.Default.AppConfig), Encoding.UTF8);
                await CompanySetupService.SyncPeerNodesToMasterAsync();
            }
            res.StatusCode = 200;
            return true;
        }

        // Chat (streaming proxy)
        if (path == "/api/chat" && req.HttpMethod == "POST")
        {
            string targetUrl = string.IsNullOrEmpty(config.MasterNodeUrl) ? "http://127.0.0.1:5050" : config.MasterNodeUrl;
            string username = req.Headers["X-Username"] ?? "未知员工";
            username = Uri.UnescapeDataString(username);

            res.ContentType = "text/plain; charset=utf-8";
            res.SendChunked = true;
            using var writer = new StreamWriter(res.OutputStream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(req.InputStream);
            string body = await reader.ReadToEndAsync();

            try
            {
                var proxyReq = new HttpRequestMessage(HttpMethod.Post, targetUrl.TrimEnd('/') + "/api/chat");
                proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(username));
                string currentTeamUrl = $"http://{req.Url?.Host}:{req.Url?.Port}";
                proxyReq.Headers.Add("X-Team-Url", Uri.EscapeDataString(currentTeamUrl));
                proxyReq.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var proxyRes = await httpClient.SendAsync(proxyReq, HttpCompletionOption.ResponseHeadersRead);
                proxyRes.EnsureSuccessStatusCode();
                using var proxyStream = await proxyRes.Content.ReadAsStreamAsync();
                await proxyStream.CopyToAsync(res.OutputStream);
            }
            catch (HttpRequestException httpEx)
            {
                string errDetail = $"[通信拦截] 无法连接到节点！\n试图请求的地址: {targetUrl}/api/chat\n底层报错: {httpEx.Message}\n\n排查建议：\n1. 检查对应的皮皮虾节点是否已启动？\n2. 检查填写的 IP 和端口是否完全一致？\n3. 如果跨电脑访问，请检查防火墙。";
                var errMsg = JsonSerializer.Serialize(new ChatResponse { type = "final", content = errDetail }, AppJsonContext.Default.ChatResponse);
                await writer.WriteAsync(errMsg + "|||END|||");
            }
            catch (Exception ex)
            {
                var errMsg = JsonSerializer.Serialize(new ChatResponse { type = "final", content = $"[未知异常] 代理层发生错误: {ex.Message}" }, AppJsonContext.Default.ChatResponse);
                await writer.WriteAsync(errMsg + "|||END|||");
            }
            return true;
        }

        // Status / History / Tasks (proxy)
        if ((path == "/api/status" || path == "/api/history" || path == "/api/tasks") && req.HttpMethod == "GET")
        {
            string username = Uri.UnescapeDataString(req.Headers["X-Username"] ?? "");
            if (!config.PeerNodes.TryGetValue(username, out var nodeInfo) || string.IsNullOrEmpty(nodeInfo.Url))
            {
                res.StatusCode = 404;
                return true;
            }
            try
            {
                using var proxyReq = new HttpRequestMessage(HttpMethod.Get, nodeInfo.Url.TrimEnd('/') + path);
                proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(username));
                using var proxyRes = await httpClient.SendAsync(proxyReq);
                proxyRes.EnsureSuccessStatusCode();
                using var proxyStream = await proxyRes.Content.ReadAsStreamAsync();
                res.ContentType = "application/json; charset=utf-8";
                await proxyStream.CopyToAsync(res.OutputStream);
            }
            catch
            {
                if (path == "/api/status")
                {
                    var offlineResp = "{\"isWorking\":false, \"currentAction\":\"离线/失联\"}";
                    res.ContentType = "application/json; charset=utf-8";
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(offlineResp));
                }
                else { res.StatusCode = 500; }
            }
            return true;
        }

        // Clear / Cancel
        if ((path == "/api/clear" || path == "/api/cancel") && req.HttpMethod == "POST")
        {
            string username = Uri.UnescapeDataString(req.Headers["X-Username"] ?? "");
            string targetUrl = "";
            if (username.ToLower() == "ceo")
            {
                targetUrl = config.PeerNodes.Values.FirstOrDefault(n => !string.IsNullOrEmpty(n.Url))?.Url ?? (config.MasterNodeUrl ?? "http://127.0.0.1:5050");
            }
            else if (config.PeerNodes.TryGetValue(username, out var nodeInfo) && !string.IsNullOrEmpty(nodeInfo.Url))
            {
                targetUrl = nodeInfo.Url;
            }
            if (string.IsNullOrEmpty(targetUrl)) { res.StatusCode = 404; return true; }
            try
            {
                using var proxyReq = new HttpRequestMessage(HttpMethod.Post, targetUrl.TrimEnd('/') + path);
                proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(username));
                using var proxyRes = await httpClient.SendAsync(proxyReq);
                proxyRes.EnsureSuccessStatusCode();
                res.ContentType = "application/json; charset=utf-8";
                string statusResp = path == "/api/cancel" ? "{\"status\":\"cancelled\"}" : "{\"status\":\"cleared\"}";
                await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(statusResp));
            }
            catch (Exception ex)
            {
                res.StatusCode = 500;
                Console.WriteLine($"[{path} 异常] {ex.Message}");
            }
            return true;
        }

        // ClearAll
        if (path == "/api/clearall" && req.HttpMethod == "POST")
        {
            int successCount = 0;
            var tasks = new List<Task>();

            string hrAgentUrl = config.PeerNodes.Values.FirstOrDefault(n => !string.IsNullOrEmpty(n.Url))?.Url ?? "http://127.0.0.1:5050";
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var proxyReq = new HttpRequestMessage(HttpMethod.Post, hrAgentUrl.TrimEnd('/') + "/api/clear");
                    proxyReq.Headers.Add("X-Username", Uri.EscapeDataString("ceo"));
                    using var proxyRes = await httpClient.SendAsync(proxyReq);
                }
                catch { /* 忽略异常 */ }
            }));

            foreach (var kvp in config.PeerNodes)
            {
                if (string.IsNullOrEmpty(kvp.Value.Url)) continue;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var proxyReq = new HttpRequestMessage(HttpMethod.Post, kvp.Value.Url.TrimEnd('/') + "/api/clear");
                        proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(kvp.Key));
                        using var proxyRes = await httpClient.SendAsync(proxyReq);
                        if (proxyRes.IsSuccessStatusCode) Interlocked.Increment(ref successCount);
                    }
                    catch { /* 节点离线则忽略 */ }
                }));
            }
            await Task.WhenAll(tasks);

            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes($"{{\"status\":\"ok\", \"cleared\":{successCount}}}"));
            return true;
        }

        // Create Company (LLM-driven)
        if (path == "/api/create_company" && req.HttpMethod == "POST")
        {
            string callAgentUrl = config.PeerNodes.Values.FirstOrDefault(n => !string.IsNullOrEmpty(n.Url))?.Url ?? "http://127.0.0.1:5050";
            int successCount = 0;
            var tasks = new List<Task>();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var proxyReq = new HttpRequestMessage(HttpMethod.Post, callAgentUrl.TrimEnd('/') + "/api/clear");
                    proxyReq.Headers.Add("X-Username", Uri.EscapeDataString("ceo"));
                    using var proxyRes = await httpClient.SendAsync(proxyReq);
                }
                catch { /* 忽略 */ }
            }));

            foreach (var kvp in config.PeerNodes)
            {
                if (string.IsNullOrEmpty(kvp.Value.Url)) continue;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var proxyReq = new HttpRequestMessage(HttpMethod.Post, kvp.Value.Url.TrimEnd('/') + "/api/clear");
                        proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(kvp.Key));
                        using var proxyRes = await httpClient.SendAsync(proxyReq);
                        if (proxyRes.IsSuccessStatusCode) Interlocked.Increment(ref successCount);
                    }
                    catch { /* 节点离线则忽略 */ }
                }));
            }
            await Task.WhenAll(tasks);

            using var bodyReader = new StreamReader(req.InputStream);
            string body = await bodyReader.ReadToEndAsync();
            var reqData = JsonSerializer.Deserialize(body, typeof(CreateCompanyReq), AppJsonContext.Default) as CreateCompanyReq;
            if (reqData == null || string.IsNullOrEmpty(reqData.Description) || string.IsNullOrEmpty(reqData.MasterNodeUrl))
            {
                res.StatusCode = 400;
                return true;
            }

            string targetUrl = reqData.MasterNodeUrl.Trim();
            var prompt = "重要：不要调用任何本地工具，不要读取或修改 appsettings.json。只返回一个合法 JSON 对象，不要返回 Markdown 或解释文字。所有字符串内的双引号必须转义。\n"
                + "老板下达了开设新公司的指令，业务描述：【" + reqData.Description + "】。\n"
                + "请你作为一个高级HR兼架构师，完成以下连串任务：\n"
                + "1. 编排一个包含 1 - 21 个核心员工的团队，生成他们的名字和岗位头衔，以及详细的个人简历（包含专业背景、工作经验、性格特点等）不需要ceo，因为用户就是 ceo。\n"
                + "2. 注意：所有生成员工的 Url 必须全部统一填为 \"" + targetUrl + "\"。\n"
                + "3. 调用你的本地工具，读取并修改你自己的 appsettings.json，把这些新员工信息补充到你的 PeerNodes 字典中，PeerNodes 字典格式如下。\n"
                + "\"PeerNodes\": {\n"
                + "    \"陈智远\": {\n"
                + "      \"Name\": \"陈智远\",\n"
                + "      \"Url\": \"" + targetUrl + "\",\n"
                + "      \"Role\": \"产品经理\",\n"
                + "      \"Resume\": \"清华大学 MBA，深耕互联网产品领域 15 年。具备极强的商业敏锐度与全局战略思维，拥有多款亿级用户量“爆款”产品的从 0 到 1 及规模化增长经验。擅长在复杂业务环境下整合跨职能资源，以数据驱动决策，实现产品价值与商业利润的双重突破。\",\n"
                + "      \"Description\": \"负责产品全生命周期的战略规划与路线图制定，深度洞察市场趋势以识别商业机会。凭借丰富的行业经验，精准平衡用户体验与商业效益，通过高效的资源整合驱动产品持续创新与市场准入。\"\n"
                + "    }\n"
                + "}\n"
                + "\n"
                + "4. 彻底修改完你自己的配置后，请在最终回复中，只输出一个合法的 JSON 数组，供中控台同步使用。绝不要有任何多余的废话和 Markdown 标记。\n"
                + "5. 编写一段详细的【公司简介与对接指南】（包含对接流程、谁负责什么业务、如何协作，使用 Markdown 排版）。\n"
                + "4和5的要求格式严格如下：\n"
                + "{\n"
                + "    \"Profile\": \"这里填写你生成的 Markdown 格式的公司简介与对接指南（注意：JSON 字符串中的换行必须转义为 \\n，确保整个 JSON 格式合法）\",\n"
                + "    \"Employees\": [\n"
                + "        { \"name\": \"员工姓名\", \"Role\": \"岗位头衔\", \"Description\": \"负责的具体能力与工作任务说明\",\"Resume\": \"个人详细信息，简历介绍。\", \"Url\": \"" + targetUrl + "\" }\n"
                + "    ]\n"
                + "}\n"
                + "注意：json字段不能省略必须严谨\n";

            var chatReq = new ChatRequest { message = prompt, modelIndex = 0 };
            using var agentReq = new HttpRequestMessage(HttpMethod.Post, callAgentUrl.TrimEnd('/') + "/api/chat");
            agentReq.Headers.Add("X-Username", Uri.EscapeDataString("ceo"));
            agentReq.Content = new StringContent(JsonSerializer.Serialize(chatReq, typeof(ChatRequest), AppJsonContext.Default), Encoding.UTF8, "application/json");

            try
            {
                using var agentRes = await httpClient.SendAsync(agentReq);
                agentRes.EnsureSuccessStatusCode();
                var agentResStr = await agentRes.Content.ReadAsStringAsync();
                string finalJson = "{}";

                var parts = agentResStr.Split(new[] { "|||END|||" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    try
                    {
                        var pushMsg = JsonSerializer.Deserialize(part, typeof(ChatResponse), AppJsonContext.Default) as ChatResponse;
                        if (pushMsg != null && pushMsg.type == "final" && !string.IsNullOrEmpty(pushMsg.content))
                            finalJson = pushMsg.content;
                    }
                    catch { }
                }

                CompanySetupResult? setupResult;
                try
                {
                    setupResult = CompanySetupService.ParseCompanySetupResult(finalJson);
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"[创建公司JSON解析失败] {parseEx.Message}");
                    setupResult = new CompanySetupResult
                    {
                        Profile = "公司创建请求已发送，但大模型返回的员工 JSON 格式不完整。请重新点击[一键开设公司]，或缩短公司描述后再试。",
                        Employees = []
                    };
                }

                if (setupResult != null)
                {
                    config.CompanyProfile = setupResult.Profile;
                    if (setupResult.Employees != null && setupResult.Employees.Count > 0)
                    {
                        foreach (var t in setupResult.Employees)
                        {
                            if (!string.IsNullOrEmpty(t.name))
                            {
                                config.PeerNodes[t.name] = new NodeInfo
                                {
                                    Name = t.name,
                                    Url = t.Url,
                                    Role = t.Role,
                                    Description = t.Description,
                                    Resume = t.Resume,
                                    ModelIndex = t.ModelIndex
                                };
                            }
                        }
                    }
                    File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, typeof(AppConfig), AppJsonContext.Default), Encoding.UTF8);
                    await CompanySetupService.SyncPeerNodesToMasterAsync();
                }

                string safeProfile = JsonSerializer.Serialize(setupResult?.Profile ?? "HR太懒，没有留下任何对接指南...", AppJsonContext.Default.String);
                res.ContentType = "application/json; charset=utf-8";
                await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes($"{{\"status\":\"ok\", \"profile\": {safeProfile}}}"));
            }
            catch (Exception ex) { res.StatusCode = 500; Console.WriteLine($"[指派招人异常] {ex.Message}"); }
            return true;
        }

        // Bankruptcy
        if (path == "/api/bankruptcy" && req.HttpMethod == "POST")
        {
            config.PeerNodes.Clear();
            config.CompanyProfile = null;
            File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, typeof(AppConfig), AppJsonContext.Default), Encoding.UTF8);
            await CompanySetupService.SyncPeerNodesToMasterAsync();
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
            return true;
        }

        // Board
        if (path == "/api/board" && req.HttpMethod == "GET")
        {
            res.ContentType = "application/json; charset=utf-8";
            var boardData = config.Projects ?? [];
            await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(boardData, AppJsonContext.Default.ListProjectBoard)));
            return true;
        }
        if (path == "/api/board" && req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream);
            string body = await reader.ReadToEndAsync();
            try
            {
                var newBoard = JsonSerializer.Deserialize(body, typeof(ProjectBoard), AppJsonContext.Default) as ProjectBoard;
                if (newBoard != null)
                {
                    config.Projects ??= [];
                    bool isDispatchNeeded = false;
                    var tasksToDispatch = new List<ProjectTask>();

                    if (newBoard.Tasks != null && newBoard.Tasks.Count == 1 && string.IsNullOrEmpty(newBoard.ProjectName))
                    {
                        var updateTask = newBoard.Tasks[0];
                        var existingTask = config.Projects.SelectMany(p => p.Tasks).FirstOrDefault(t => t.Id == updateTask.Id);
                        if (existingTask != null)
                        {
                            existingTask.Status = updateTask.Status;
                            existingTask.UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            if (!string.IsNullOrEmpty(updateTask.Result)) existingTask.Result = updateTask.Result;
                        }
                    }
                    else if (!string.IsNullOrEmpty(newBoard.ProjectName))
                    {
                        isDispatchNeeded = true;
                        var existingProject = config.Projects.FirstOrDefault(p => p.ProjectName == newBoard.ProjectName);
                        if (existingProject == null)
                        {
                            config.Projects.Add(newBoard);
                            tasksToDispatch.AddRange(newBoard.Tasks);
                        }
                        else
                        {
                            if (newBoard.Tasks != null)
                            {
                                foreach (var t in newBoard.Tasks)
                                {
                                    if (string.IsNullOrEmpty(t.Id)) t.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                                    t.UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                    if (string.IsNullOrWhiteSpace(t.Assignee)) t.Assignee = "待认领";
                                    var existingT = existingProject.Tasks.FirstOrDefault(x => x.Title == t.Title || x.Id == t.Id);
                                    if (existingT == null) { existingProject.Tasks.Add(t); tasksToDispatch.Add(t); }
                                    else existingT.Status = t.Status;
                                }
                            }
                        }
                    }

                    File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, typeof(AppConfig), AppJsonContext.Default), Encoding.UTF8);

                    if (isDispatchNeeded && tasksToDispatch.Count > 0)
                    {
                        string teamUrl = $"http://{req.Url?.Host}:{req.Url?.Port}";
                        string companySop = config.CompanySOP ?? "";
                        string projectName = newBoard.ProjectName;
                        var capturedConfig = config;
                        _ = Task.Run(async () =>
                        {
                            string accumulatedContext = "";
                            foreach (var t in tasksToDispatch)
                            {
                                if (string.IsNullOrWhiteSpace(t.Assignee) || t.Assignee == "待认领" || t.Assignee.ToLower() == "ceo") continue;
                                if (capturedConfig.PeerNodes.TryGetValue(t.Assignee, out var nodeInfo) && !string.IsNullOrEmpty(nodeInfo.Url))
                                {
                                    try
                                    {
                                        string taskTargetUrl = nodeInfo.Url.TrimEnd('/') + "/api/agent_task";
                                        string execPrompt = $"【系统最高指令】\n所属项目：{projectName}\n你的任务目标：{t.Title}\n";
                                        if (!string.IsNullOrWhiteSpace(accumulatedContext))
                                            execPrompt += $"\n【前置任务交付的上下文参考】（请基于以下结果继续推进你的工作）：\n{accumulatedContext}\n";
                                        execPrompt += "\n请立刻开始执行此任务。完成后请直接用自然语言输出结果，底层会自动为你更新项目看板。";
                                        var proxyReq = new HttpRequestMessage(HttpMethod.Post, taskTargetUrl);
                                        proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(t.Assignee));
                                        proxyReq.Headers.Add("X-Team-Url", Uri.EscapeDataString(teamUrl));
                                        var reqBody = new ChatRequest { message = execPrompt, modelIndex = nodeInfo.ModelIndex, caller = "ceo", taskId = t.Id, sop = companySop };
                                        proxyReq.Content = new StringContent(JsonSerializer.Serialize(reqBody, typeof(ChatRequest), AppJsonContext.Default), Encoding.UTF8, "application/json");
                                        using var proxyRes = await httpClient.SendAsync(proxyReq);
                                        proxyRes.EnsureSuccessStatusCode();
                                        string taskResult = await proxyRes.Content.ReadAsStringAsync();
                                        accumulatedContext += $"\n--- 同事 [{t.Assignee}] 完成了前置任务 [{t.Title}]，交付成果如下 ---\n{taskResult}\n";
                                        await Task.Delay(3000);
                                    }
                                    catch (Exception ex) { Console.WriteLine($"[中控后台派发异常] 派发给 {t.Assignee} 失败: {ex.Message}"); accumulatedContext += $"\n--- 警告：同事 [{t.Assignee}] 的前置任务 [{t.Title}] 执行异常 ---\n系统反馈：{ex.Message}\n"; }
                                }
                            }
                        });
                    }
                }
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
            }
            catch { res.StatusCode = 500; }
            return true;
        }
        if (path == "/api/board" && req.HttpMethod == "DELETE")
        {
            config.Projects = [];
            File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, typeof(AppConfig), AppJsonContext.Default), Encoding.UTF8);
            res.ContentType = "application/json; charset=utf-8";
            await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
            return true;
        }

        // Agent task proxy
        if (path == "/api/agent_task" && req.HttpMethod == "POST")
        {
            string username = Uri.UnescapeDataString(req.Headers["X-Username"] ?? "");
            string targetUrl = string.IsNullOrEmpty(config.MasterNodeUrl) ? "http://127.0.0.1:5050" : config.MasterNodeUrl;
            if (config.PeerNodes.TryGetValue(username, out var nodeInfo) && !string.IsNullOrEmpty(nodeInfo.Url))
                targetUrl = nodeInfo.Url;

            using var reader = new StreamReader(req.InputStream);
            string body = await reader.ReadToEndAsync();

            try
            {
                var proxyReq = new HttpRequestMessage(HttpMethod.Post, targetUrl.TrimEnd('/') + "/api/agent_task");
                proxyReq.Headers.Add("X-Username", Uri.EscapeDataString(username));
                string currentTeamUrl = $"http://{req.Url?.Host}:{req.Url?.Port}";
                proxyReq.Headers.Add("X-Team-Url", Uri.EscapeDataString(currentTeamUrl));
                proxyReq.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var proxyRes = await httpClient.SendAsync(proxyReq);
                proxyRes.EnsureSuccessStatusCode();
                string proxyResStr = await proxyRes.Content.ReadAsStringAsync();
                res.ContentType = "text/plain; charset=utf-8";
                await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(proxyResStr));
            }
            catch (Exception ex) { res.StatusCode = 500; Console.WriteLine($"[代理 AgentTask 异常] {ex.Message}"); }
            return true;
        }

        return false;
    }
}
