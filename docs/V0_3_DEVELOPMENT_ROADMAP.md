# v0.3 开发路线与回测计划

日期：2026-06-09  
目标：把当前世界杯 AI 公司重构为战术桌风格、产品级可用、数据可追溯的足球 AI Agent 系统。

## 1. 总原则

- 每个阶段只解决一个层级的问题。
- 每个阶段必须 build 和 smoke test。
- 不把工程内部对象直接暴露给用户。
- 不为了 UI 动画破坏后端清晰边界。
- 不让 LLM 直接给概率。
- 所有概率必须伴随置信度、风险和数据质量。
- 所有 LLM 调用必须记录成本和输出。
- 所有赛后结果必须能进入记忆和回测。

## 2. 当前基线

已确认：

- 后端可构建。
- 当前 React 可构建。
- 生产视图：48 队 / 72 场小组赛 / 0 demo 混入。
- 数据源审计可用。
- baseline 预测可用。
- Memory 基础表和 API 可用。
- 方向 B 战术桌已被选为视觉基准。

需要注意：

- 现有源文件有中文乱码。
- 当前 UI 仍不是最终信息架构。
- 部分 FIFA rank 为占位。
- 淘汰赛尚未完整。

## 3. 阶段 1：产品 BFF 与 DTO

### 目标

新增面向 UI 的产品 DTO，不让前端继续拼底层对象。

### 文件

建议新增：

```text
Api/ProductBffApi.cs
Features/Product/WorldCupStore.ProductOverview.cs
Features/Product/WorldCupStore.ProductMatches.cs
Domain/Models/ProductViewModels.cs
```

或在现有文件中小步扩展：

```text
Api/BffApi.cs
Features/WorldCup/WorldCupStore.Dashboard.cs
```

### 接口

必须实现：

```text
GET /api/worldcup/product/overview
GET /api/worldcup/product/matches
GET /api/worldcup/product/matches/{match_id}
GET /api/worldcup/product/data-trust
GET /api/worldcup/product/audit
```

可后置：

```text
POST /api/worldcup/product/matches/{match_id}/refresh
```

### DTO 字段

比赛卡片：

- match_id
- group_name
- kickoff_time
- venue
- status
- home_team
- away_team
- probabilities
- confidence
- risk_level
- data_quality
- summary

比赛详情：

- match
- teams
- employees
- probabilities
- factors
- evidence
- risks
- memories
- model_review
- ceo_summary
- audit

### 回测

```text
dotnet build --nologo
GET /api/worldcup/product/overview
GET /api/worldcup/product/matches
GET /api/worldcup/product/matches/{first_match_id}
GET /api/worldcup/data-readiness-audit
```

验收：

- 0 warning / 0 error。
- JSON 可解析。
- 72 场比赛返回。
- 第一个 match detail 有概率、证据、风险、审计摘要。
- 不混入 demo/harness。

## 4. 阶段 2：前端数据层

### 目标

建立新 UI 的数据访问层。

### 文件

```text
Frontend/worldcup-ui/src/product/types.ts
Frontend/worldcup-ui/src/product/api.ts
Frontend/worldcup-ui/src/product/teamNames.ts
```

### 回测

```text
npm.cmd run build
npx.cmd tsc --noEmit
```

验收：

- 类型完整。
- API 错误有中文 fallback。
- 不再从 UI 组件里直接组合底层 API。

## 5. 阶段 3：战术桌 UI 骨架

### 目标

替换当前主要 UI 为：

- 顶部导航。
- 左侧近期赛程预测队列。
- 中央战术桌。
- 右侧预测检查器。

### 文件

```text
Frontend/worldcup-ui/src/App.tsx
Frontend/worldcup-ui/src/styles.css
Frontend/worldcup-ui/src/components/tactical/TopNavigation.tsx
Frontend/worldcup-ui/src/components/tactical/MatchQueue.tsx
Frontend/worldcup-ui/src/components/tactical/TacticalTable.tsx
Frontend/worldcup-ui/src/components/tactical/PredictionInspector.tsx
```

### 回测

```text
npm.cmd run build
浏览器打开 http://127.0.0.1:5174/
桌面检查
390px 移动检查
```

验收：

- 首页默认显示最近一场比赛。
- 赛程队列可点击切换。
- UI 中文无乱码。
- 主界面不出现 workflow/artifact/harness。
- 桌面和移动无横向溢出。

## 6. 阶段 4：交互抽屉

### 目标

补齐信息但不污染主界面。

### 抽屉

- 证据详情。
- 员工工作台。
- 审计日志。
- 数据源说明。

### 回测

- 点击证据卡打开详情。
- 点击员工打开工作台。
- 点击审计打开内部记录。
- 关闭后回到战术桌。

## 7. 阶段 5：单场刷新闭环

### 目标

用户点击“刷新这场比赛”后完成真实闭环。

### 后端动作

```text
collect match-related data
triage signals
build memory context
refresh prediction
run LLM review when budget allows
write artifact
write memory
return updated match detail
```

### 前端状态

- 准备刷新。
- 数据采集中。
- 员工分拣中。
- 策略计算中。
- 模型审查中。
- 研报归档。
- 完成/失败。

### 回测

- 模型在线时可生成报告。
- 模型离线时降级为无 LLM 报告，但策略概率仍可用。
- token 成本可见。
- 记忆写入可查。

## 8. 阶段 6：记忆强化

### 目标

把记忆从“记录”升级为“预测上下文”。

### 后端

新增或完善：

```text
BuildMatchMemoryContext(match_id)
WritePredictionMemory(match_id)
WritePostMatchReviewMemory(match_id)
MarkContradictedMemory(...)
GetProductMemoryHints(match_id)
```

### 回测

- 创建预测后有 match_prediction_memory。
- 录入赛果后有 post_match_review_memory。
- 下一次刷新同队比赛时能召回相关记忆。
- 过期新闻记忆不进入 prompt。

## 9. 阶段 7：预测策略补强与回测

### 目标

提升“值得相信”的程度。

### 工作

- 补真实球队强度数据。
- 加入近期战绩。
- 加入数据质量对 confidence 的影响。
- 建立回测报告。

### 指标

- 命中率。
- Brier Score。
- Log Loss。
- calibration。
- 与 `snapshot_aware_v1` 比较。

## 10. 阶段 8：素材和动效

### 目标

让战术桌具备最终产品质感。

### 素材

- 战术桌背景。
- 员工形象。
- 资料卡。
- 风险章。
- 终端屏。
- 球队代码牌。

### 动效

- 比赛切换。
- 证据卡进入。
- 概率盘变化。
- 员工状态亮起。
- 风险章盖印。

### 回测

- 动画不影响主信息。
- 低性能模式可关闭。
- mobile 不错位。

## 11. 风险与控制

### 风险：UI 变漂亮但信息仍乱

控制：

- 先做 BFF。
- 前端只消费产品 DTO。
- 主界面只围绕一场比赛。

### 风险：LLM token 消耗不可控

控制：

- 切换比赛不调用 LLM。
- 只有刷新/生成研报才调用。
- prompt 使用压缩证据和 top memories。
- 每次调用前估算 token。

### 风险：数据源不稳定

控制：

- 数据源分层。
- 快照去重。
- 失败降级。
- UI 显示“待补数据”。

### 风险：预测被误解为投注建议

控制：

- 文案明确“研判，不是投注建议”。
- 风险和置信度必须伴随概率。
- 不输出“稳赚”“稳胆”等词。

## 12. 立即执行建议

下一步开始阶段 1。

具体任务：

1. 新增产品 DTO。
2. 新增 `/api/worldcup/product/matches`。
3. 新增 `/api/worldcup/product/matches/{id}`。
4. 用当前 production 数据生成中文比赛卡片。
5. build + smoke test。

阶段 1 通过后，再重构 React UI。
