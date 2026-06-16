# 世界杯 AI 公司 v0.3 架构与开发依据

日期：2026-06-09  
状态：v0.3 开发基准  
目标项目：`D:\PPclaw\PiPiClaw.Team`

## 1. 产品定位

世界杯 AI 公司不是一个普通后台，也不是一个单纯聊天机器人。它是一个面向足球赛事的 AI Agent 研判系统：

- 自动采集公开数据。
- 一个球队绑定一个长期 AI 研究员。
- 每场比赛生成可追溯的胜平负概率。
- 大模型负责证据压缩、风险审查、研报表达和复盘记忆。
- 策略模型负责概率计算，避免让 LLM 直接拍胜率。
- 赛后结果进入记忆和回测，让系统下一轮判断更稳。

最终体验要像“可交互的战术桌 + AI 研究公司”，而不是满屏表格。

## 2. v0.3 明确方向

用户已选择第二个 UI 方向：战术桌。

v0.3 的 UI 定版原则：

- 顶部保留清晰导航：比赛预测、球队研究室、数据可信度、研报归档。
- 左侧采用“近期赛程 / 按时间排序的预测队列”，参考方向 A。
- 中间采用战术桌主视觉，展示当前选中比赛的预测、证据卡、员工协作。
- 右侧展示预测结论、风险、来源台账和模型审查摘要。
- 审计和工程概念收进抽屉，不进入主视觉。
- 必须大量使用足球/战术/员工/资料卡视觉素材，但信息层次要简洁高效。

## 3. 当前系统可复用能力

已存在且应保留：

- `WorldCupWatchObject`：球队对象。
- `WorldCupEmployee`：AI 员工。
- `EmployeeAssignment`：球队与员工绑定。
- `WorldCupMatch`：比赛。
- `BaselinePredictionRecord`：策略层概率。
- `DataSnapshotRecord`：数据快照。
- `IntelligenceSignalRecord`：情报信号。
- `MemoryRecord`：长期记忆。
- `ArtifactRecord`：报告产物。
- `WorldCupSystemEventLog`：系统日志。
- `BffApi`：前端聚合接口入口。
- `WorldCupStore.*`：SQLite 持久化与领域服务。
- `LlmGateway`：PiPiClaw/DeepSeek 模型调用。

当前必须修复的问题：

- 前端和部分 C# 源文件存在中文乱码。
- React 前端一次性拉太多底层数据，缺少产品级 BFF。
- 当前 UI 暴露 workflow、artifact、harness 等工程概念。
- 部分球队排名为占位值 50，预测可信度必须降级。
- 淘汰赛阶段未完整建模。
- 记忆系统已有基础，但还没有成为预测闭环的一部分。

## 4. 核心领域模型

### 4.1 赛事对象

```text
Tournament
  Stage: pre_tournament / group / knockout / completed
  Groups: A-L
  Matches: group matches + generated knockout matches
```

### 4.2 球队对象

```text
Team
  id
  中文名
  英文名
  FIFA code
  flag
  group
  status: active / advanced / eliminated / archived
  strength profile
  data quality
```

### 4.3 员工对象

```text
Employee
  id
  name
  role
  assigned_team_id
  prompt profile
  memory scope
  model budget
  status
```

### 4.4 比赛预测对象

```text
MatchPredictionView
  match
  home_team
  away_team
  probabilities
  confidence
  risk_level
  strategy_factors
  evidence_pack
  memory_context
  model_review
  ceo_summary
  audit_summary
```

这是 v0.3 前端真正应该消费的对象。

## 5. 分层架构

```text
React/Vite Tactical UI
    |
    v
Product BFF API
    |
    +-- Match Prediction Query
    +-- Team Workbench Query
    +-- Data Trust Query
    +-- Report Query
    +-- Audit Drawer Query
    |
Application Services
    |
    +-- Data Collection Service
    +-- Intelligence Triage Service
    +-- Memory Service
    +-- Prediction Service
    +-- LLM Review Service
    +-- Report Service
    +-- Backtest Service
    |
Domain
    |
    +-- Teams
    +-- Matches
    +-- Strategies
    +-- Memories
    +-- Evidence
    +-- Employees
    |
Infrastructure
    |
    +-- SQLite
    +-- Public Data Source Adapters
    +-- PiPiClaw/DeepSeek LLM Gateway
    +-- File Artifacts
```

v0.3 不引入微服务，不引入复杂消息队列。当前规模下 `.NET + SQLite + React/Vite` 足够，重点是把边界理清。

## 6. 产品级 BFF 接口

### 6.1 首页概览

```text
GET /api/worldcup/product/overview
```

返回：

- 当前阶段。
- 球队总数、比赛总数。
- 可预测比赛数。
- 今日/近期比赛。
- 高关注比赛。
- 数据源健康。
- LLM 成本摘要。
- 记忆写入/召回摘要。

### 6.2 比赛预测队列

```text
GET /api/worldcup/product/matches?from=&to=&group=&status=&limit=
```

返回左侧赛程队列所需的轻量卡片：

```json
{
  "match_id": "match_wc26_7",
  "group_name": "C 组",
  "kickoff_time": "2026-06-13T22:00:00Z",
  "venue": "New York/New Jersey Stadium",
  "home": { "id": "team_bra", "name_cn": "巴西", "code": "BRA", "flag": "..." },
  "away": { "id": "team_mar", "name_cn": "摩洛哥", "code": "MAR", "flag": "..." },
  "prediction": { "home": 0.706, "draw": 0.211, "away": 0.082 },
  "confidence": "medium",
  "risk_level": "medium",
  "data_quality": "needs_strength_update",
  "summary": "巴西明显占优，但客队强度数据仍需补强。"
}
```

### 6.3 单场战术桌详情

```text
GET /api/worldcup/product/matches/{match_id}
```

返回：

- 比赛基本信息。
- 双方球队卡。
- 员工信息。
- 胜平负概率。
- 策略因子。
- 证据包。
- 记忆上下文。
- LLM 审查摘要。
- CEO 结论。
- 风险提示。
- 审计摘要。

### 6.4 单场刷新

```text
POST /api/worldcup/product/matches/{match_id}/refresh
```

执行：

1. 获取相关球队。
2. 按需采集公开数据。
3. 变化检测。
4. 生成情报信号。
5. 构建记忆上下文。
6. 策略层刷新概率。
7. 必要时调用 LLM 审查。
8. 写入报告与记忆。
9. 返回新的单场详情。

### 6.5 球队研究室

```text
GET /api/worldcup/product/teams/{team_id}
```

返回：

- 球队档案。
- 专属员工。
- 小组赛程。
- 历史报告。
- 记忆摘要。
- 情报变化。
- 数据质量。

### 6.6 数据可信度

```text
GET /api/worldcup/product/data-trust
```

返回：

- 数据源列表。
- 权威级别。
- 最新采集时间。
- 快照数量。
- 可用于哪些判断。
- 不能用于哪些判断。
- 是否需要 API key。

### 6.7 审计抽屉

```text
GET /api/worldcup/product/audit?match_id=&team_id=
```

返回：

- workflow 简要。
- artifact 简要。
- LLM 调用。
- token 成本。
- 系统日志。
- 原始快照引用。

该接口只给高级抽屉，不进入默认主界面。

## 7. 数据采集与可信度

数据源按用途分层：

| 层级 | 用途 | 示例 | 入模方式 |
|---|---|---|---|
| 官方参考 | 赛制、赛程校验 | FIFA 官方页面 | 人工/半自动参考 |
| 结构化赛程 | 自动导入比赛和球队 | FixtureDownload、openfootball、worldcup26 feed | 可直接入库 |
| 实力指标 | 排名、Elo、近期表现 | FIFA ranking、公开 Elo/历史战绩源 | 策略层核心输入 |
| 新闻线索 | 伤病、阵容、舆情 | RSS、公开新闻 | 先分拣，不直接入模 |
| 赔率市场 | 校准概率 | 未来可选，需要 key | 可选高级信号 |

v0.3 首要补强：

- 真实球队中文名映射。
- FIFA rank 占位识别。
- 数据质量等级。
- 每场比赛显示“该预测为什么可信/为什么不够可信”。

## 8. 预测策略设计

原则：

- 策略层给概率。
- LLM 给解释和审查。
- 记忆给历史上下文。
- 赛后结果给回测和策略改进。

推荐 v0.3 策略输入：

```text
team_strength:
  fifa_rank
  elo_rating
  recent_form
  goal_difference
  opponent_adjusted_form

match_context:
  venue
  travel_distance
  group_stage_incentive
  rest_days

intelligence:
  injury_risk
  lineup_confidence
  coaching_change
  weather_or_pitch

memory:
  prior_prediction_errors
  team_specific_lessons
  recurring_risk_patterns
```

输出：

```text
home_win_probability
draw_probability
away_win_probability
confidence
risk_level
factor_explanations
data_gaps
```

## 9. 记忆系统设计

记忆不是聊天历史，而是长期可召回的判断资产。

### 9.1 记忆类型

```text
team_profile_memory
  球队长期风格、强弱、反复出现的问题。

match_prediction_memory
  某场比赛赛前预测、因子、风险和最终判断。

post_match_review_memory
  赛后结果、预测偏差、错因分析。

strategy_memory
  某个策略版本的表现、适用场景和缺陷。

employee_memory
  某个员工长期负责对象的观察偏好和历史产出。
```

### 9.2 写入时机

必须写入：

- 单场预测生成后。
- LLM 审查后。
- CEO 总结后。
- 比赛结果录入后。
- 回测发现策略偏差后。
- 球队晋级/出局后。

### 9.3 召回时机

必须召回：

- 构建球队资料包时。
- 刷新比赛预测时。
- 生成 LLM 审查 prompt 时。
- 赛后复盘时。
- 策略版本比较时。

### 9.4 记忆压缩

为了节约 token：

- 只召回 top 6-10 条。
- 按 object_id、employee_id、strategy、match_id 过滤。
- 用摘要进入 prompt，不把全文塞给模型。
- 过期新闻类记忆必须设置 expires_at。
- 赛后复盘类记忆长期保留。

### 9.5 记忆冲突

当新事实推翻旧事实：

- 不删除旧记忆。
- 写入新记忆。
- 旧记忆设置 `contradicted_by_memory_id`。
- UI 中显示“已被更新事实覆盖”。

## 10. Agent 协作流程

单场比赛刷新流程：

```text
用户点击“刷新这场比赛”
  -> Data Collector 收集相关球队和比赛数据
  -> Intelligence Triage 分拣新闻和变化
  -> Home Team Researcher 生成主队摘要
  -> Away Team Researcher 生成客队摘要
  -> Memory Service 召回历史经验
  -> Prediction Strategy 计算概率
  -> Risk Officer 检查数据缺口和过度自信
  -> LLM Reviewer 生成可读解释
  -> CEO 生成最终研判
  -> Report Service 写入 artifact
  -> Memory Service 写入预测记忆
  -> UI 更新战术桌
```

## 11. 测试与回测门禁

每个阶段必须有 harness：

### 11.1 数据门禁

- 数据源可访问。
- JSON 可解析。
- 快照 hash 正常。
- demo/harness 不混入生产。
- 来源质量能被 UI 显示。

### 11.2 预测门禁

- 每场比赛概率之和为 1。
- 概率在 0-1。
- 占位数据会降低 confidence。
- 策略因子可解释。
- 赛后结果能进入回测。

### 11.3 记忆门禁

- 预测后写入记忆。
- 赛后复盘写入记忆。
- 过期记忆不召回。
- 冲突记忆可标记。
- prompt 只使用压缩记忆。

### 11.4 LLM 门禁

- 模型离线时降级。
- 调用前估算 token。
- 调用后记录成本。
- 输出必须包含结论、证据、不确定性、行动建议。
- 禁止输出投注建议式措辞。

### 11.5 UI 门禁

- 桌面和移动无横向溢出。
- 中文无乱码。
- 首屏可理解当前比赛和预测结论。
- 工程术语不出现在主界面。
- 左侧赛程队列可搜索/筛选。
- 战术桌区域可点击证据卡、员工和概率。

## 12. 分阶段开发计划

### 阶段 1：产品 BFF

目标：

- 新增 `ProductBffApi` 或扩展 `BffApi`。
- 输出 overview、matches、match detail。
- 保证中文名、概率、数据质量、风险等级可用。

验收：

- `dotnet build` 通过。
- 新接口 JSON 字段稳定。
- 生产数据仍为 48 队 / 72 小组赛 / 0 demo。

### 阶段 2：战术桌 UI 骨架

目标：

- 顶部导航。
- 左侧近期赛程预测队列。
- 中央战术桌主区域。
- 右侧预测/风险/来源。
- 审计抽屉隐藏。

验收：

- `npm run build` 通过。
- 浏览器桌面/移动检查通过。
- 无中文乱码。

### 阶段 3：单场刷新闭环

目标：

- 单场刷新按钮。
- 自动采集相关数据。
- 生成/更新预测。
- 写入记忆。
- 返回最新 match detail。

验收：

- 刷新前后 audit 有记录。
- token 成本可见。
- 记忆可召回。

### 阶段 4：预测模型补强

目标：

- 引入更可靠球队强度数据。
- 替换占位 rank。
- 加入近期战绩。
- 建立基本回测指标。

验收：

- Brier Score/Log Loss 可计算。
- 策略版本可比较。
- UI 显示“比上一版提升/退步”。

### 阶段 5：素材与动效系统

目标：

- 统一战术桌视觉资产。
- 球队员工形象。
- 证据卡动画。
- 概率盘动画。
- 晋级/出局员工状态变化。

验收：

- 素材目录结构稳定。
- 组件不依赖硬编码图片路径。
- 低性能设备可关闭动效。

## 13. 工程约束

- 不一次性推倒后端。
- 不把 LLM 当数据库。
- 不把新闻线索直接当事实。
- 不在主 UI 暴露 harness/workflow/artifact。
- 不为了动画牺牲可读性。
- 所有新接口先有稳定 DTO。
- 所有关键能力必须可回测。
- 每次阶段完成必须 build + API smoke test + browser smoke test。

## 14. v0.3 推荐目录增量

```text
Api
  ProductBffApi.cs

Features
  Product
    WorldCupStore.ProductOverview.cs
    WorldCupStore.ProductMatches.cs
  Prediction
    PredictionConfidence.cs
    PredictionFactorMapper.cs

Frontend/worldcup-ui/src
  product/
    api.ts
    types.ts
  components/tactical/
    TopNavigation.tsx
    MatchQueue.tsx
    TacticalTable.tsx
    PredictionPanel.tsx
    EvidenceCards.tsx
    AgentDesk.tsx
    AuditDrawer.tsx
  assets/
    tactical/
```

## 15. 成功标准

v0.3 完成后，用户打开系统应该能在 10 秒内理解：

- 现在有多少比赛可预测。
- 下一场重点比赛是谁对谁。
- 胜平负概率是多少。
- 为什么系统这么判断。
- 哪些数据还不够可信。
- 哪些 AI 员工参与了判断。
- 大模型是否调用成功、花了多少钱。
- 这个判断以后如何被赛后结果修正。

这就是 v0.3 的产品核心。
