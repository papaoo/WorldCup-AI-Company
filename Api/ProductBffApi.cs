using System.Net;

namespace PiPiClaw.Team;

/// <summary>
/// Product-facing APIs for the World Cup tactical table UI.
/// These endpoints return Chinese, user-facing DTOs and hide internal workflow storage terms.
/// </summary>
public static class ProductBffApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "/";
        var store = AppContext.WorldCupStore;

        if (path == "/api/worldcup/product/overview" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 24;
            var result = store.GetProductOverview(req.QueryString["selected_match_id"], limit);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ProductOverviewResult);
            return true;
        }

        if (path == "/api/worldcup/product/matches" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 72;
            var result = store.GetProductMatchQueue(limit);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ListProductMatchQueueItem);
            return true;
        }

        if (path == "/api/worldcup/product/teams/research" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["match_limit"], out var parsedLimit) ? parsedLimit : 200;
            var result = store.GetProductTeamResearch(limit);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ProductTeamResearchResult);
            return true;
        }

        if (path.StartsWith("/api/worldcup/product/matches/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase)
            && req.HttpMethod == "POST")
        {
            var matchId = ExtractMatchId(path);
            if (string.IsNullOrWhiteSpace(matchId))
            {
                await ApiHelpers.WriteErrorAsync(res, "match_id is required");
                return true;
            }

            try
            {
                store.CreateBaselinePrediction(matchId);
                var result = store.GetProductMatchDetail(matchId);
                if (result == null)
                {
                    await ApiHelpers.WriteErrorAsync(res, "match not found", 404);
                    return true;
                }

                await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ProductMatchDetail);
                return true;
            }
            catch (Exception ex)
            {
                await ApiHelpers.WriteErrorAsync(res, ex.Message);
                return true;
            }
        }

        if (path.StartsWith("/api/worldcup/product/matches/", StringComparison.OrdinalIgnoreCase)
            && req.HttpMethod == "GET")
        {
            var matchId = ExtractMatchId(path);
            if (string.IsNullOrWhiteSpace(matchId))
            {
                await ApiHelpers.WriteErrorAsync(res, "match_id is required");
                return true;
            }

            var result = store.GetProductMatchDetail(matchId);
            if (result == null)
            {
                await ApiHelpers.WriteErrorAsync(res, "match not found", 404);
                return true;
            }

            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ProductMatchDetail);
            return true;
        }

        if (path == "/api/worldcup/product/data-trust" && req.HttpMethod == "GET")
        {
            var result = store.GetProductDataTrust();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ListProductDataTrustItem);
            return true;
        }

        if (path == "/api/worldcup/product/model-health" && req.HttpMethod == "GET")
        {
            var result = store.GetProductModelHealth();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ProductModelHealthResult);
            return true;
        }

        if (path == "/api/worldcup/product/audit" && req.HttpMethod == "GET")
        {
            var result = store.GetProductAudit(req.QueryString["match_id"], req.QueryString["team_id"]);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ProductAuditResult);
            return true;
        }

        return false;
    }

    private static string ExtractMatchId(string path)
    {
        var prefix = "/api/worldcup/product/matches/";
        var value = path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..]
            : "";
        if (value.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^"/refresh".Length];
        }
        return Uri.UnescapeDataString(value.Trim('/'));
    }
}
