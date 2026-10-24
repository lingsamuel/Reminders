# Reminders

Windows 10/11 上的简易任务 Overlay。编辑器使用 Tauri + React，支持独立重置时间的重复任务，以及设置截止日期的一次性任务。任务引擎与原生透明 Overlay 由 .NET 后台提供，保持唯一的周期与存储实现。

## 使用

1. 启动 `Reminders.exe`，新建任务，或使用 Epic 每周、日常 05:00、日常 04:00、每 2 天模板。
2. 编辑器打开时，拖动 Overlay 标题调整位置，拖动右下角调整大小；在设置页调整字号、透明度与置顶。
3. 关闭或最小化编辑器后，程序驻留系统托盘。Overlay 只有勾选框接收点击，文字和其他区域穿透到底层程序；勾选不切换键盘焦点。
4. 勾选后，该任务在本周期立即隐藏。全部完成时整个 Overlay 隐藏，新周期开始后重新出现。需要撤销时，从托盘打开编辑器。
5. 游戏独占全屏、无边框全屏及浏览器/视频全屏时自动隐藏，恢复窗口模式后恢复显示。自动隐藏优先于置顶设置。
6. 通过托盘菜单“退出 Reminders”结束程序。关闭编辑器按钮用于进入托盘。

## 日程规则

| 类型 | 示例 | 行为 |
| --- | --- | --- |
| 每日 | 每日 05:00 | 04:59 仍属于前一周期，05:00 开始新周期 |
| 每周 | 每周四 00:00 | 周四零点开始新的七天周期 |
| 每 N 天 | 从 10 月 24 日起，每 2 天 04:00 | 24、26、28 日 04:00 重置；完成时间不改变后续周期 |
| 一次性 | 10 月 31 日截止 | 不选具体时间时表示当天结束；逾期保持显示，完成后不会循环 |

每个任务独立计算，没有统一的全局重置点。未完成的重复任务跨周期后继续显示本周期的一条任务，不累计欠账。未到起始日期的每 N 天任务暂不显示。

所有时间跟随操作系统当前时区，支持系统时间变更与休眠恢复。夏令时缺失时间顺延至首个有效分钟，重复时间使用首次出现的时刻。每 N 天按本地日历计算，不保证间隔严格为 N×24 小时。

修改重复规则或任务类型会开始新的日程版本，并提示重置当前完成状态；改名、排序及修改一次性截止时间不会撤销完成状态。

历史页保留完成与撤销事件，包括操作时的任务名称、周期/截止快照和时区；归档不删除历史。历史中的绝对时刻使用当前系统时区显示。不提供历史补签或未完成周期的统计。

## 获取与构建

发布包解压后运行 `Reminders.exe`，请保留同目录的 `backend` 子目录。无需另装 .NET；编辑器需要 Windows WebView2 Runtime，安装包可处理该运行时的安装。

开发需要 .NET 10 SDK、Node.js 22+、Rust 稳定版和 Windows C++ 构建工具。Windows 开发先生成后台，再启动编辑器：

```powershell
dotnet test tests/Reminders.Tests -c Release
dotnet test tests/Reminders.Windows.Tests/Protocol/Reminders.Protocol.Tests.csproj -c Release
dotnet build Reminders.slnx -c Release
dotnet publish src/Reminders.Windows -c Release -r win-x64 --self-contained true -o artifacts/backend
cd src/Reminders.Desktop
npm ci
npm test
npm run tauri dev
```

仅预览 React 编辑器可运行 `npm run dev`。浏览器会明确显示演示模式，不连接真实任务文件，也不模拟周期计算。桌面功能以 Tauri 模式为准。

从仓库根目录生成 Windows x64 安装包和便携 ZIP：

```powershell
./scripts/publish.ps1
```

便携包输出为 `artifacts/Reminders-0.1.0-win-x64.zip`；NSIS 安装包在 Tauri 的 `target/x86_64-pc-windows-msvc/release/bundle/nsis`。项目没有自动更新；替换程序前先从托盘退出，用户数据不会保存在程序目录。

Linux/WSL 可运行核心、协议与 React 测试，交叉编译 .NET 后台与 Tauri Windows GNU 目标；完整应用面向 Windows。后台交叉发布命令：

```sh
dotnet publish src/Reminders.Windows -c Release -r win-x64 --self-contained true -o artifacts/backend
```

已安装 MinGW-w64 和 Rustup 的 Linux/WSL 环境可运行 `./scripts/publish-cross.sh` 直接生成 Windows 便携 ZIP，不启动任何应用窗口。

GitHub Actions 会运行核心/协议/前端测试、离屏验证、无界面进程验证和 Windows 构建，上传 ZIP 与安装包，不自动创建正式 Release。

## 数据与恢复

默认数据目录是 `%LOCALAPPDATA%\Reminders`。保存使用同目录临时文件和原子替换，`state.json.bak` 保留上一次有效版本。操作保存失败时不会从 Overlay 隐藏任务。

遇到损坏数据时，编辑器会显示错误并提供需要明确确认的备份恢复入口。恢复前建议复制整个数据目录；也可选择退出自行检查。遇到不支持的数据版本会拒绝加载，避免旧程序覆盖新版本数据。错误日志会在 1 MiB 后轮换。

可选启动参数：

```powershell
Reminders.exe --start-hidden
Reminders.exe --data-dir "D:\RemindersData"
```

`--start-hidden` 直接进入托盘与日常模式；`--data-dir` 使用指定数据目录，便于便携运行与隔离验收。同一目录只允许一个后台持有数据锁。首版不自动配置开机启动。

## 行为范围与验收

- 前台应用在任一显示器全屏时，整个 Overlay 隐藏；后台全屏窗口不单独触发。检测间隔约 500 毫秒。
- 普通最大化窗口保留任务栏时，Overlay 仍显示。系统演示模式和锁屏状态也会隐藏 Overlay。
- 日常模式自动扩展高度至当前屏幕工作区；超过可容纳数量时提示剩余任务数，通过编辑器查看完整列表。
- 支持负屏幕坐标与 PerMonitorV2 DPI；显示器变化时把 Overlay 调整回可见工作区。
- 本程序没有游戏注入、键盘钩子或游戏接口。

开发与实机检查记录见 [验证说明](docs/verification.md)，进程边界和接口见 [架构说明](docs/architecture.md)。所有自动化默认不干扰共享桌面；真实鼠标、焦点及全屏验收只在隔离环境进行。
