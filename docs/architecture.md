# 架构与本地接口

## 单一任务引擎

React 编辑器通过 Tauri Rust 桥接访问 .NET 后台。Core 是唯一的周期、完成与日程版本实现；Persistence 是唯一的磁盘格式实现。WPF 仅用于原生透明 Overlay，不再提供编辑器。

React 的浏览器演示适配器只能处理明确标识的演示数据，不能写本地文件，也不能被当作桌面验收。正式桌面必须经由 Tauri 调用后台。

## JSONL 协议

后台 stdin 每行接收一个 UTF-8 请求：

```json
{"id":1,"method":"complete","payload":{"id":"任务 GUID"}}
```

成功回复 `{"id":1,"result":{...快照...}}`，失败回复 `{"id":1,"error":"中文说明"}`。stdout 只包含 JSON，诊断信息写 stderr 或数据目录日志。Rust 以 id 关联响应，连接失败或超时必须 reject 前端操作。

快照包含进程内单调递增的 `sequence`、`ready`、`tasks`（当前非归档任务投影）、`definitions`（全部定义，用于历史筛选）、`events`、`overlay`、`timeZoneId`、`timeZoneLabel`、`now`。同一状态的响应和事件共享版本；前端仅接纳不早于已见版本的快照，避免旧请求晚返回时覆盖新状态。字段为 camelCase；日程和操作枚举序列化为英文名称；本地日期为 ISO 日期，绝对时刻为含偏移的 ISO 时刻。此协议不改变 Persistence 现有文件格式。

后台错误另有前端错误代际保护：错误发生前启动的请求和迟到订阅不能把界面重新标为可用；只有错误之后主动发起且期间未发生新错误的重读成功，才允许恢复写入。新建表单在挂载时固定任务 ID，超时重试仍是同一任务的保存，不会生成重复任务。

| 方法 | payload | 行为 |
| --- | --- | --- |
| snapshot | `{}` | 读取当前快照；仅未就绪时重试读取主文件，成功后重建原生运行时 |
| saveTask | `{task}` | 新建/编辑；后台决定日程版本 |
| complete / undo / archive | `{id}` | 完成/撤销/归档 |
| setEnabled | `{id,enabled}` | 启用或停用 |
| move | `{id,delta}` | 按偏移排序 |
| updateSettings | `{settings}` | 更新 Overlay 设置 |
| editorVisible | `{visible}` | 切换 Overlay 的编辑/日常模式 |
| restoreBackup | `{}` | 用户明确确认后恢复备份 |
| exit | `{}` | 结束后台 |

所有成功方法返回快照。加载损坏文件时保持协议连接，返回 `ready=false`、`error` 与 `canRestoreBackup`，由编辑器提供恢复入口；不弹出老式 WPF 对话框，也不静默重建数据。用户在外部修复主文件后，可用“重新读取”重试；该操作不自动恢复备份、不保存文件。已就绪的普通 snapshot 不重建 TaskService。

后台事件：`ready`/`changed` 附带 `snapshot`，Rust 转发为 `backend-state`；`openEditor` 由用户点击托盘触发，Rust 显示编辑器并通知后台；`exit` 结束桌面应用；`error` 转发为 `backend-error`。

## 窗口与退出

编辑器关闭/最小化都进入托盘，用户明确选择退出才结束进程。编辑模式的 Overlay 阻止意外 Alt+F4 销毁，统一由后台退出流程关闭。文字 HWND 在日常模式完全穿透；每个可见勾选框是非激活小窗口，其点击先持久化，成功后才移除。

Tauri 正常退出关闭 stdin，后台收到 EOF 释放窗口、托盘和独占数据锁；超时后 Rust 清理自己启动的子进程。后台不可用时，前端拒绝写操作并报告错误。

## 测试边界

- Core/Persistence 自动化不依赖 Windows。
- React 测试和截图只使用 jsdom 或 headless 浏览器。
- Windows 离屏检查仅创建未显示的控件树，不调用 Show/Activate，不触发真实输入。
- 真正的跨进程穿透、焦点、全屏与多屏验收必须在隔离的 Windows Sandbox/虚拟机中进行。
