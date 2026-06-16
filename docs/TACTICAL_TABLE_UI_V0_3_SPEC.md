# 战术桌 UI v0.3 设计规范

日期：2026-06-09  
状态：已选定方向  
参考方向：`design-demos/demo-2-tactical-table.html` + `demo-1-command-center.html` 左侧赛程队列

## 1. 设计读法

这是一个中文用户使用的世界杯 AI 预测产品，不是后台管理系统。

设计关键词：

- 战术桌
- 球探研究室
- AI 员工公司
- 公开数据证据链
- 胜率预测
- 赛前 briefing
- 可追溯审计

视觉应该像“赛事研究团队围着战术桌工作”，而不是普通 dashboard。

## 2. 页面总体布局

```text
TopNavigation
  比赛预测 / 球队研究室 / 数据可信度 / 研报归档

MainLayout
  Left: MatchQueue
  Center: TacticalTable
  Right: PredictionInspector

Overlay
  EvidenceDetailDrawer
  EmployeeWorkbenchDrawer
  AuditDrawer
```

## 3. 顶部导航

参考用户截图中的圆角导航风格。

导航项：

- 比赛预测
- 球队研究室
- 数据可信度
- 研报归档

右侧状态：

- 数据更新时间。
- 模型网关状态。
- 今日 token 成本。
- 自动采集状态。

按钮文案必须是用户目标，不要是工程动作：

- “刷新这场比赛”
- “生成赛前研报”
- “查看证据来源”
- “打开审计”

禁止主导航出现：

- workflow
- artifact
- harness
- snapshot
- seed
- mock

## 4. 左侧近期赛程队列

左侧采用方向 A 的近期赛程队列，不再使用方向 B 原始的小组卡列表。

标题：

```text
近期赛程
按时间排序的预测队列
```

功能：

- 按时间排序。
- 显示小组、开球时间、场馆。
- 显示双方中文名和 FIFA code。
- 显示三项概率中的最高项。
- 显示数据质量标记。
- 支持搜索球队。
- 支持筛选：全部、今日、可预测、待补数据、高风险。

卡片内容：

```text
A 组 · 6 月 12 日
墨西哥 MEX 37.0%
南非 RSA 37.0%
数据质量：中低
```

状态样式：

- 可预测：绿色/石灰色。
- 待补数据：琥珀色。
- 高风险：红色小标。
- 已结束：低饱和灰色。

## 5. 中央战术桌

中央是主视觉和核心交互，不是装饰。

组成：

- 当前比赛标题。
- 双方球队徽章/旗帜/代码。
- 战术桌背景。
- 概率盘。
- 双方研究员桌位。
- 数据分析师桌位。
- 风险官桌位。
- CEO 批注卡。
- 证据卡散布在桌面上。

布局原则：

- 一屏只聚焦一场比赛。
- 中央概率盘必须清楚。
- 证据卡不超过 5 张，更多放抽屉。
- 员工不是列表，是带角色的工作位。
- 素材丰富但不干扰数字阅读。

## 6. 右侧预测检查器

右侧是理性解释区。

模块顺序：

1. 最终判断。
2. 胜平负概率条。
3. 置信度与风险。
4. 策略因子。
5. 数据来源。
6. 模型审查。
7. 记忆引用。

示例：

```text
CEO 判断
巴西明显占优，但摩洛哥强度数据仍需补真值，因此结论标记为中等置信。

概率
巴西胜 70.6%
平局 21.1%
摩洛哥胜 8.2%

风险
客队排名为占位值
缺少近期战绩
阵容未确认
```

## 7. 素材规范

### 7.1 必要素材

第一批素材应包括：

- 战术桌背景。
- 木质桌边或深色桌面。
- 绿色球场桌布。
- 资料卡纸张。
- 员工 Q 版头像。
- 员工桌位。
- 数据终端。
- 风险警示章。
- CEO 批注纸。
- 球队徽章/代码牌。

### 7.2 素材风格

风格：

- 半写实游戏 UI。
- 温暖但不幼稚。
- 细节丰富但轮廓清晰。
- 更像“球探工作室”，不是儿童卡通。

避免：

- 简陋圆角卡片。
- 大面积单调深色。
- 低质渐变。
- 随机图标堆叠。
- 背景和内容混在一起看不清。

### 7.3 素材目录

```text
Frontend/worldcup-ui/public/tactical-assets/
  table/
    tactical-table-bg.png
    table-edge.png
    pitch-lines.svg
  cards/
    evidence-card-paper.png
    ceo-note.png
    risk-stamp.png
  employees/
    analyst-home.png
    analyst-away.png
    analyst-data.png
    analyst-risk.png
    analyst-ceo.png
  teams/
    fallback-crest.svg
```

## 8. 交互设计

### 8.1 选择比赛

点击左侧比赛：

- 中央战术桌切换比赛。
- 右侧预测检查器刷新。
- URL 更新 `?match_id=...`。
- 不自动调用 LLM，避免消耗 token。

### 8.2 刷新比赛

点击“刷新这场比赛”：

执行产品 BFF 单场刷新：

- 采集相关数据。
- 分拣变化。
- 生成预测。
- 必要时调用 LLM。
- 写入记忆。

UI 状态：

- 数据采集中。
- 员工分拣中。
- 策略计算中。
- 模型审查中。
- 研报已生成。

### 8.3 点击证据卡

打开证据抽屉：

- 来源。
- 原始摘要。
- 可靠度。
- 捕获时间。
- 是否参与概率计算。
- 是否只是新闻线索。

### 8.4 点击员工

打开员工工作台：

- 负责球队。
- 最新资料包。
- 最近记忆。
- 最近报告。
- 待处理信号。

### 8.5 打开审计

审计是角落按钮，不抢主视觉。

显示：

- workflow。
- artifact。
- LLM 调用。
- token 成本。
- 系统日志。

## 9. 记忆 UI

记忆不是聊天记录。UI 要表达为“系统经验”。

右侧展示：

```text
历史记忆
- 阿根廷强弱分明场次曾出现慢热风险。
- 日本队资料包上次缺少阵容确认，导致置信度降级。
- 赛程源可用于时间校验，但不能判断球队强弱。
```

记忆卡字段：

- 类型。
- 重要性。
- 置信度。
- 更新时间。
- 来源。
- 是否被新事实覆盖。

## 10. 数据可信度 UI

数据可信度页面不是给技术人员看的日志，而是回答“我能不能相信这个系统”。

显示：

- 源名称。
- 权威层级。
- 稳定性。
- 最新采集。
- 快照数。
- 用途。
- 禁止用途。
- 是否需要 key。

文案示例：

```text
openfootball
适合：赛程交叉校验
不适合：球队强度、伤病、阵容
可靠度：0.78
```

## 11. 响应式设计

桌面：

```text
顶部导航
左 320px / 中间自适应 / 右 360px
```

平板：

```text
顶部导航
左侧赛程横向滚动
中央战术桌
右侧折叠为抽屉
```

手机：

```text
顶部导航横向滚动
比赛队列变成顶部横滑
战术桌纵向堆叠
预测检查器默认折叠
```

必须验证：

- 390px 宽度无横向溢出。
- 概率数字不换行溢出。
- 中文按钮不挤压。
- 证据卡不遮挡概率盘。

## 12. 视觉 token

建议色彩：

```text
background: #0f0d0a / #151810
felt green: #133728
wood: #2a2116
paper: #e8dcc2
lime accent: #a8ff70
gold accent: #f2c66d
blue accent: #75d7ff
danger: #ff7d6b
```

字体：

- 中文主字体：系统中文黑体。
- 数字：tabular numeric。
- 不引入在线字体，避免网络依赖。

圆角：

- 主容器：28-34px。
- 卡片：20-24px。
- 小标签：999px。

动效：

- 切换比赛：桌面证据卡淡入和轻微位移。
- 刷新流程：员工桌位状态亮起。
- 概率变化：概率盘平滑转动。
- 风险升高：风险章盖印动效。

动效只用 transform 和 opacity。

## 13. 内容规范

中文优先。

推荐术语：

- 比赛预测
- 球队研究室
- 数据可信度
- 研报归档
- 证据包
- 预测队列
- 模型审查
- 历史记忆
- 风险提示
- 单场刷新

避免术语：

- workflow
- artifact
- harness
- seed
- mock
- snapshot
- run loop

如果必须出现，放入审计抽屉。

## 14. 组件拆分

```text
App
  TacticalShell
    TopNavigation
    MatchQueue
    TacticalTable
      TeamDesk
      EmployeeDesk
      ProbabilityWheel
      EvidenceCard
      CeoNote
    PredictionInspector
      ProbabilityBars
      RiskList
      FactorList
      SourceLedger
      MemoryHints
    Drawers
      EvidenceDrawer
      EmployeeDrawer
      AuditDrawer
```

每个组件只关心产品 DTO，不直接拼底层 API。

## 15. 首版验收标准

首版实现完成后必须满足：

- 打开首页默认展示最近一场比赛。
- 左侧赛程来自真实生产接口。
- 中文球队名正常。
- 中央战术桌显示双方、概率、证据、员工。
- 右侧显示 CEO 判断、风险、来源、记忆。
- 点击比赛能切换详情。
- 点击刷新能触发后端刷新。
- 构建通过。
- Playwright/浏览器检查桌面与移动无明显错位。

## 16. 推荐开发顺序

1. 新增产品 DTO 和 BFF。
2. React 新增 product api/types。
3. 实现 TopNavigation 和 MatchQueue。
4. 实现 TacticalTable 静态骨架。
5. 接入单场详情。
6. 接入刷新流程。
7. 接入证据/员工/审计抽屉。
8. 补素材。
9. 做动效。
10. 做移动适配。

不要先做复杂动画。先让信息结构正确、可读、可用，再增加动效。
