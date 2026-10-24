# Reminders 实现计划

> **For agentic workers:** 使用 superpowers:subagent-driven-development 或 superpowers:executing-plans 执行；按下列复选框记录验证与交付。

**Goal:** 实现带编辑器、托盘、历史与自动隐藏 Overlay 的 Windows 任务工具。

**Architecture:** Core 统一提供周期、任务和完成记录语义；Persistence 使用版本化 JSON 原子保存；WPF 编辑器与 Overlay 共享同一任务服务。原生平台层负责全屏识别、穿透、非激活窗口和显示器适配，不复制业务状态。

**Tech Stack:** .NET 10、WPF、Windows Forms NotifyIcon、Win32、xUnit。

## 项目划分与契约

- `src/Reminders.Core`：`TaskDefinition`、`Schedule`、`TaskPeriod`、`CompletionEvent`、`AppState`、`OverlaySettings`；`ScheduleCalculator`、`TaskService`、`IStateStore`。
- `src/Reminders.Persistence`：`JsonStateStore`，实现 `IStateStore`。
- `src/Reminders.Windows`：应用生命周期、`EditorWindow`、`TaskEditDialog`、`OverlayWindow`、`OverlayController` 与 `Platform` 服务。
- `tests/Reminders.Tests`：周期、状态事务、文件恢复与平台无关显示策略测试。
- `scripts/publish.ps1`、`.github/workflows/build.yml`：Windows 发布与自动化验证。

数据模型采用可序列化记录；每次变更生成新状态，保存成功后才替换服务状态并触发 Changed 事件。所有 UI 使用 TaskService，无独立的完成逻辑。

## Task 1：工程与核心日程

- [ ] 建立解决方案、共享编译设置、忽略文件与测试项目，初版版本为 0.1.0。
- [ ] 先写周期用例：每日 04:00/05:00、周四 00:00、每两天跨月、未来起点、DST 缺失/重复时间、一次性到期后仍存在。
- [ ] 运行 `dotnet test tests/Reminders.Tests`，确认缺失功能导致失败。
- [ ] 实现唯一的周期计算入口；使用 DateOnly/TimeOnly 表达本地规则，DateTimeOffset 表达绝对时刻。
- [ ] 每日和每周找出不晚于当前时刻的最近边界；每 N 天按起点日期的日数差整除；DST 无效时间按分钟前移至有效时间，歧义时间选择较大偏移（更早 UTC）。
- [ ] 验证 `dotnet test tests/Reminders.Tests` 通过。

## Task 2：任务事务与持久化

- [ ] 测试先行：完成、撤销、重启、一次性截止变更保留完成、重复规则变更重置、停用/归档、中文、损坏文件与备份恢复、保存失败状态不变。
- [ ] TaskService 注入 TimeProvider、TimeZoneInfo 提供器和 IStateStore；统一返回当前任务投影，历史按 UTC 时间倒序。
- [ ] 保存时在同目录写临时文件并 Flush，随后原子替换并保留 `.bak`。读取损坏主文件时抛出可识别异常；用户确认后才读取并恢复备份，绝不静默覆盖。
- [ ] 校验 SchemaVersion、枚举值、唯一 ID、周期参数、窗口几何信息和事件引用；较新数据版本拒绝覆盖。
- [ ] 测试通过后提交完整核心与持久化功能。

## Task 3：Windows 应用与编辑器

- [ ] WPF 工程使用 `net10.0-windows`，启用 WPF、Windows Forms、Windows 交叉编译支持，显式配置 PerMonitorV2。
- [ ] 编辑器提供任务、历史、显示设置三个页面，复用 TaskService 读取与更新；表单按任务类型启用相关参数。
- [ ] 实现新增/修改/完成/撤销/排序/停用/归档；保存会改变当前重复周期时提示，取消不修改状态。
- [ ] 历史使用事件快照显示名称、周期/截止、完成/撤销时间、当时时区，支持任务过滤。
- [ ] 实现四种场景快捷模板与一次性任务创建。
- [ ] 最小化和关闭进入托盘；双击托盘恢复；菜单含显示开关与退出。命名互斥量保护单实例，正常退出释放资源。

## Task 4：Overlay 与原生平台

- [ ] 主文字窗口在日常模式设置 `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`；每个可见勾选框使用一个小型独立顶层非激活窗口，避免伪造跨进程穿透。
- [ ] 勾选窗口通过 `WM_MOUSEACTIVATE = MA_NOACTIVATE` 保持焦点；点击调用 TaskService.Complete。位置由文字窗口行容器 PointToScreen 计算，使用物理像素 SetWindowPos 对齐。
- [ ] 编辑模式主窗口显示标题、背景、Resize Thumb，允许 DragMove 和缩放；仅编辑模式保存布局，日常自动扩展不覆盖用户设定尺寸。
- [ ] 只在任务列表、模式、设置、周期或显示器变化时重建视图，500ms 定时器仅采样全屏和当前投影差异，避免持续重建窗口。
- [ ] 前台窗口非自身/桌面/任务栏时，结合 SHQueryUserNotificationState、DWM 可见边框、显示器矩形识别独占和无边框全屏；过滤最小化与普通最大化。
- [ ] 隐藏优先级：全屏 > 用户隐藏 > 日常无待办；自动恢复不得激活窗口或改变用户设置。
- [ ] 系统时间/电源/显示器事件通过 Dispatcher 安全刷新；显示器断开后纠正不可见坐标；日常溢出显示剩余任务数。
- [ ] 运行 Windows 目标 Release 构建，处理所有编译错误与警告。

## Task 5：交付与审查

- [ ] 编写中文 README、操作说明、时间语义、数据恢复与 Windows 实机验收清单。
- [ ] 添加 Windows CI：核心测试、Release 构建与自包含 win-x64 发布；发布脚本支持本地生成 ZIP。
- [ ] 独立进行规格审查，再进行代码质量审查；修复影响正确性和需求覆盖的问题。
- [ ] 执行 `dotnet test tests/Reminders.Tests -c Release`、`dotnet build Reminders.slnx -c Release` 和 win-x64 发布。
- [ ] 记录实测结果；Linux 无法验证的 Windows UI 行为明确保留为待实机验收。
- [ ] 提交可独立交付的改动，并在当前工作区保留可构建源码及本地发布产物。
