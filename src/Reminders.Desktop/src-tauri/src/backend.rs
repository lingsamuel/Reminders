use crate::protocol::{decode_response, encode_request};
use crate::visibility::VisibilityCoordinator;
use serde_json::{json, Value};
use std::{
    collections::HashMap,
    io::{BufRead, BufReader, Write},
    path::PathBuf,
    process::{Child, ChildStdin, Command, Stdio},
    sync::{
        atomic::{AtomicBool, AtomicU64, Ordering},
        mpsc, Arc, Mutex,
    },
    thread,
    time::Duration,
};
use tauri::{AppHandle, Emitter, Manager};

type Pending = HashMap<u64, mpsc::SyncSender<Result<Value, String>>>;

pub struct Backend {
    child: Mutex<Child>,
    stdin: Mutex<Option<ChildStdin>>,
    pending: Mutex<Pending>,
    next_id: AtomicU64,
    stopping: AtomicBool,
    expected_exit: AtomicBool,
    disconnected: AtomicBool,
    visibility: VisibilityCoordinator,
}

impl Backend {
    pub fn start(app: AppHandle, arguments: &[String]) -> Result<Arc<Self>, String> {
        let path = backend_path(&app)?;
        let mut command = Command::new(&path);
        command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        command
            .arg("--editor-pid")
            .arg(std::process::id().to_string());
        if let Some(index) = arguments.iter().position(|arg| arg == "--data-dir") {
            let directory = arguments
                .get(index + 1)
                .filter(|arg| !arg.starts_with("--"))
                .ok_or("--data-dir 后需要提供数据目录")?;
            command.arg("--data-dir").arg(directory);
        }
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000); // CREATE_NO_WINDOW：stdio 后端不创建控制台。
        }
        let mut child = command
            .spawn()
            .map_err(|error| format!("无法启动任务后端 {}：{error}", path.display()))?;
        let stdin = child.stdin.take().ok_or("无法连接后端输入")?;
        let stdout = child.stdout.take().ok_or("无法连接后端输出")?;
        let stderr = child.stderr.take();
        let backend = Arc::new(Self {
            child: Mutex::new(child),
            stdin: Mutex::new(Some(stdin)),
            pending: Mutex::new(HashMap::new()),
            next_id: AtomicU64::new(1),
            stopping: AtomicBool::new(false),
            expected_exit: AtomicBool::new(false),
            disconnected: AtomicBool::new(false),
            visibility: VisibilityCoordinator::default(),
        });
        let weak = Arc::downgrade(&backend);
        let events = app.clone();
        thread::spawn(move || {
            for line in BufReader::new(stdout).lines() {
                let Ok(line) = line else { break };
                let Ok(value) = serde_json::from_str::<Value>(&line) else {
                    continue;
                };
                let Some(backend) = weak.upgrade() else { break };
                if let Some(id) = value.get("id").and_then(Value::as_u64) {
                    if let Some(sender) = backend.pending.lock().unwrap().remove(&id) {
                        let _ = sender.send(decode_response(&value));
                    }
                } else {
                    match value.get("event").and_then(Value::as_str) {
                        Some("changed" | "ready") => {
                            let _ = events.emit("backend-state", value.get("snapshot"));
                        }
                        Some("openEditor") => {
                            let _ = send_visibility(backend.clone(), events.clone(), true, true);
                        }
                        Some("exit") => {
                            backend.expected_exit.store(true, Ordering::SeqCst);
                            events.exit(0);
                        }
                        Some("error") => {
                            let _ = events.emit("backend-error", value.get("message"));
                        }
                        _ => {}
                    }
                }
            }
            if let Some(backend) = weak.upgrade() {
                backend.disconnected.store(true, Ordering::SeqCst);
                for (_, sender) in backend.pending.lock().unwrap().drain() {
                    let _ = sender.send(Err("任务后端已停止，请退出并重新启动 Reminders".into()));
                }
                if !backend.stopping.load(Ordering::SeqCst)
                    && !backend.expected_exit.load(Ordering::SeqCst)
                {
                    backend.visibility.set(true);
                    let _ = backend.visibility.synchronize(|_| {
                        restore_editor(&events, "任务后端已停止，请退出并重新启动 Reminders");
                        Ok(())
                    });
                }
            }
        });
        if let Some(stderr) = stderr {
            thread::spawn(move || {
                for line in BufReader::new(stderr).lines().map_while(Result::ok) {
                    eprintln!("backend: {line}");
                }
            });
        }
        Ok(backend)
    }

    pub fn request(&self, method: &str, payload: Value) -> Result<Value, String> {
        if self.stopping.load(Ordering::SeqCst) {
            return Err("程序正在退出".into());
        }
        if self.disconnected.load(Ordering::SeqCst) {
            return Err("任务后端已停止，请退出并重新启动 Reminders".into());
        }
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        let line = encode_request(id, method, payload)?;
        let (sender, receiver) = mpsc::sync_channel(1);
        self.pending
            .lock()
            .map_err(|_| "后端连接失效")?
            .insert(id, sender);
        let write_result = self
            .stdin
            .lock()
            .map_err(|_| "后端连接失效")?
            .as_mut()
            .ok_or_else(|| "后端连接已关闭".to_string())
            .and_then(|stream| {
                stream
                    .write_all(line.as_bytes())
                    .and_then(|()| stream.flush())
                    .map_err(|error| error.to_string())
            });
        if let Err(error) = write_result {
            self.pending.lock().unwrap().remove(&id);
            return Err(error);
        }
        let result = receiver
            .recv_timeout(Duration::from_secs(10))
            .map_err(|_| "任务后端响应超时，请确认当前状态后重试".to_string());
        self.pending.lock().unwrap().remove(&id);
        result?
    }

    pub fn stop(&self) {
        if self.stopping.swap(true, Ordering::SeqCst) {
            return;
        }
        // 关闭 stdin 让后端按 EOF 正常释放托盘与数据锁；超时后才强制清理本进程的子进程。
        self.stdin.lock().unwrap().take();
        if let Ok(mut child) = self.child.lock() {
            for _ in 0..20 {
                if child.try_wait().ok().flatten().is_some() {
                    return;
                }
                thread::sleep(Duration::from_millis(50));
            }
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

pub fn send_visibility(
    backend: Arc<Backend>,
    app: AppHandle,
    visible: bool,
    focus: bool,
) -> tauri::async_runtime::JoinHandle<Result<(), String>> {
    backend.visibility.set(visible);
    tauri::async_runtime::spawn_blocking(move || {
        backend.visibility.synchronize_or_recover(
            |latest| {
                backend.request("editorVisible", json!({"visible":latest}))?;
                if let Some(window) = app.get_webview_window("main") {
                    if latest {
                        window.unminimize().map_err(|e| e.to_string())?;
                        window.show().map_err(|e| e.to_string())?;
                        if focus {
                            window.set_focus().map_err(|e| e.to_string())?;
                        }
                    } else {
                        window.hide().map_err(|e| e.to_string())?;
                    }
                }
                Ok(())
            },
            |error| restore_editor(&app, error),
        )
    })
}

fn restore_editor(app: &AppHandle, error: &str) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
    }
    let _ = app.emit("backend-error", error);
}

fn backend_path(app: &AppHandle) -> Result<PathBuf, String> {
    if let Some(path) = std::env::var_os("REMINDERS_BACKEND_PATH") {
        return Ok(PathBuf::from(path));
    }
    if let Ok(directory) = app.path().resource_dir() {
        let path = directory.join("backend").join("Reminders.Backend.exe");
        if path.is_file() {
            return Ok(path);
        }
    }
    let development = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../../artifacts/backend/Reminders.Backend.exe");
    if development.is_file() {
        return Ok(development);
    }
    Err("找不到任务后端。请先运行发布脚本生成完整程序包。".into())
}
