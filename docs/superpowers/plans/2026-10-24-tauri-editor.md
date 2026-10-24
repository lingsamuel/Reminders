# Tauri + React 编辑器交付计划

> 使用 subagent-driven-development 与 dispatching-parallel-agents：前端、后台协议与 Rust 桥接按独立目录分工，统一审查与集成。该计划接续已完成的 Core/Persistence 实现，替代原计划中的 WPF 编辑器部分。

**Goal:** 按用户明确选择，交付现代 React 编辑器，同时复用已验证的任务引擎和原生 Overlay。

**Architecture:** React 只渲染后台快照。Tauri Rust 管理子进程、关联 JSONL 请求并串行同步窗口模式；.NET 后台统一管理数据、周期、托盘和 Overlay。所有测试默认不干扰用户桌面。

**Tech Stack:** Tauri 2、React 19、TypeScript、Vite、Rust、.NET 10、WPF Overlay。

## 交付步骤

- [x] 记录已确认的技术变更、进程边界、JSONL 结构与窗口生命周期。
- [x] 保留 Core/Persistence，移除 WPF 编辑器；提取 BackendSession、JsonLineProtocol 与启动参数。
- [x] 后台提供快照、任务管理、历史、显示设置、明确备份恢复与编辑器可见状态接口。
- [x] 提供 `--headless`，验证真实进程通信、数据锁、EOF 清理与重启恢复，不创建任何可见窗口或托盘。
- [x] 实现暖白/深绿 React 编辑器：任务卡片、模板、抽屉表单、历史、显示设置、失败禁写及明确演示模式。
- [x] 建立前端测试与 headless 浏览器视觉检查；不复制周期计算。
- [x] 实现 Rust 请求关联、响应超时、后台退出清理与资源路径解析。
- [x] 修复规格审查发现的加载失败托盘、显隐请求乱序、错误恢复和后台退出后隐藏窗口问题。
- [x] 验证 Windows GNU 目标编译，便携包包含自包含后台和动态 WebView2 加载库。
- [x] 完成最后代码质量审查，修复快照版本、错误代际、重新读取和设置草稿问题。
- [x] 运行交付前验证，生成 Windows 便携 ZIP，同步文档与独立提交。

## 验证命令

```sh
dotnet test tests/Reminders.Tests -c Release
dotnet test tests/Reminders.Windows.Tests/Protocol/Reminders.Protocol.Tests.csproj -c Release
dotnet build Reminders.slnx -c Release
cd src/Reminders.Desktop
npm test
npm run build
```

Windows 原生工具链中运行 `cargo test --locked` 与 `scripts/publish.ps1`；Linux/WSL 使用 `scripts/publish-cross.sh` 交叉发布。真正的输入穿透、焦点和硬件全屏验收仍保留给隔离 Windows 环境，不用共享桌面代替。
