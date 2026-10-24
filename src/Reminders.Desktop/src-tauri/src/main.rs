#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod backend;
mod protocol;
mod visibility;

use backend::{send_visibility, Backend};
use serde_json::Value;
use std::sync::Arc;
use tauri::{Manager, State};

#[tauri::command]
async fn backend_request(
    backend: State<'_, Arc<Backend>>,
    method: String,
    payload: Option<Value>,
) -> Result<Value, String> {
    // stdio 请求在阻塞线程运行，不阻塞 WebView/窗口事件线程。
    let backend = backend.inner().clone();
    tauri::async_runtime::spawn_blocking(move || {
        backend.request(&method, payload.unwrap_or_else(|| serde_json::json!({})))
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn hide_editor(
    window: tauri::WebviewWindow,
    backend: State<'_, Arc<Backend>>,
) -> Result<(), String> {
    send_visibility(
        backend.inner().clone(),
        window.app_handle().clone(),
        false,
        false,
    )
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
fn exit_application(app: tauri::AppHandle) {
    app.exit(0);
}

fn main() {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let hidden = arguments.iter().any(|arg| arg == "--start-hidden");
    let app = tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            backend_request,
            hide_editor,
            exit_application
        ])
        .setup(move |app| {
            let backend =
                Backend::start(app.handle().clone(), &arguments).map_err(std::io::Error::other)?;
            app.manage(backend.clone());
            if !hidden {
                let _ = send_visibility(backend, app.handle().clone(), true, false);
            }
            Ok(())
        })
        .on_window_event(|window, event| {
            if window.label() != "main" {
                return;
            }
            match event {
                tauri::WindowEvent::CloseRequested { api, .. } => {
                    api.prevent_close();
                    if let Some(backend) = window.app_handle().try_state::<Arc<Backend>>() {
                        let _ = send_visibility(
                            backend.inner().clone(),
                            window.app_handle().clone(),
                            false,
                            false,
                        );
                    }
                }
                tauri::WindowEvent::Resized(_) if window.is_minimized().unwrap_or(false) => {
                    if let Some(backend) = window.app_handle().try_state::<Arc<Backend>>() {
                        let _ = send_visibility(
                            backend.inner().clone(),
                            window.app_handle().clone(),
                            false,
                            false,
                        );
                    }
                }
                _ => {}
            }
        })
        .build(tauri::generate_context!())
        .expect("无法初始化 Reminders 桌面窗口");
    app.run(|app, event| {
        if matches!(event, tauri::RunEvent::Exit) {
            if let Some(backend) = app.try_state::<Arc<Backend>>() {
                backend.stop();
            }
        }
    });
}
