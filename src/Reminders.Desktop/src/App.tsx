import { useCallback, useEffect, useRef, useState } from "react";
import {
  AlertCircle,
  Check,
  CircleHelp,
  CloudOff,
  History as HistoryIcon,
  Leaf,
  ListTodo,
  Loader2,
  LogOut,
  Minus,
  Monitor,
  ShieldCheck,
  Sprout,
  X,
} from "lucide-react";
import { api } from "./lib/api";
import type {
  BackendApi,
  BackendMethod,
  Snapshot,
  TaskDefinition,
} from "./lib/types";
import { errorText } from "./lib/format";
import { TaskBoard } from "./components/TaskBoard";
import type { TaskTemplate } from "./components/TaskBoard";
import { TaskEditor } from "./components/TaskEditor";
import { History } from "./components/History";
import { OverlaySettings } from "./components/OverlaySettings";

type Page = "tasks" | "history" | "settings";
interface Confirmation {
  title: string;
  description: string;
  label: string;
  action: () => Promise<void>;
}

export function App({ backend = api }: { backend?: BackendApi }) {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [loadError, setLoadError] = useState("");
  const [page, setPage] = useState<Page>("tasks");
  const [busy, setBusy] = useState(false);
  const [editor, setEditor] = useState<{
    task?: TaskDefinition;
    template?: TaskTemplate;
  } | null>(null);
  const [toast, setToast] = useState<{ text: string; error?: boolean } | null>(
    null,
  );
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const notifyError = useCallback(
    (error: unknown) => setToast({ text: errorText(error), error: true }),
    [],
  );
  const currentSequence = useRef(-1);
  const errorEpoch = useRef(0);
  const connectionError = useRef<string | null>(null);
  // 状态版本决定数据新旧；错误代际决定请求是否仍属于当前有效连接。
  const applySnapshot = useCallback(
    (state: Snapshot, requestEpoch?: number, recover = false) => {
      if (requestEpoch !== undefined && requestEpoch !== errorEpoch.current)
        return false;
      if (state.sequence < currentSequence.current) return false;
      currentSequence.current = state.sequence;
      if (connectionError.current !== null && !recover) {
        // 错误后迟到的订阅事件可更新数据，但恢复写入须由主动重读确认。
        setSnapshot({
          ...state,
          ready: false,
          error: connectionError.current,
          canRestoreBackup: false,
        });
        return true;
      }
      connectionError.current = null;
      setSnapshot(state);
      setLoadError("");
      return true;
    },
    [],
  );

  useEffect(() => {
    let active = true;
    let stop: (() => void) | undefined;
    async function connect() {
      let requestEpoch = errorEpoch.current;
      try {
        const unsubscribe = await backend.subscribe(
          (state) => {
            if (active) {
              applySnapshot(state);
            }
          },
          (message) => {
            if (active) {
              errorEpoch.current += 1;
              connectionError.current = message;
              setLoadError(message);
              setSnapshot((state) =>
                state
                  ? {
                      ...state,
                      ready: false,
                      error: message,
                      canRestoreBackup: false,
                    }
                  : state,
              );
            }
          },
        );
        if (!active) {
          unsubscribe();
          return;
        }
        stop = unsubscribe;
        requestEpoch = errorEpoch.current;
        const state = await backend.request("snapshot");
        if (active) {
          applySnapshot(state, requestEpoch);
        }
      } catch (error) {
        if (active && requestEpoch === errorEpoch.current)
          setLoadError(errorText(error));
      }
    }
    void connect();
    return () => {
      active = false;
      stop?.();
    };
  }, [backend, applySnapshot]);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(
      () => setToast(null),
      toast.error ? 8000 : 3500,
    );
    return () => window.clearTimeout(timer);
  }, [toast]);

  // 仅成功且未过期的快照可更新界面；持久化失败时保留原状态与编辑内容。
  const request = async (
    method: BackendMethod,
    payload: Record<string, unknown> = {},
  ) => {
    const requestEpoch = errorEpoch.current;
    setBusy(true);
    try {
      const state = await backend.request(method, payload);
      const accepted = applySnapshot(
        state,
        requestEpoch,
        method === "snapshot",
      );
      if (accepted && !state.ready)
        throw new Error(state.error || "后台当前无法保存，请先恢复数据");
    } finally {
      setBusy(false);
    }
  };
  const action = (method: BackendMethod, payload: Record<string, unknown>) => {
    void request(method, payload).catch(notifyError);
  };
  const closeEditor = () => setEditor(null);
  const hide = () => {
    void backend.hide().catch(notifyError);
  };
  const askArchive = (task: TaskDefinition) =>
    setConfirmation({
      title: "把这件事收起来？",
      description: `「${task.title}」将从任务列表和桌面提醒中移除，历史记录会保留。`,
      label: "确认归档",
      action: async () => {
        await request("archive", { id: task.id });
      },
    });
  const runConfirmation = async () => {
    if (!confirmation) return;
    try {
      await confirmation.action();
      setConfirmation(null);
    } catch (error) {
      notifyError(error);
    }
  };
  const pending =
    snapshot?.tasks.filter(
      (status) =>
        status.task.enabled &&
        status.isAvailable &&
        !status.isCompleted &&
        !status.task.archived,
    ).length ?? 0;
  const date = snapshot?.now ?? new Date().toISOString();
  const dateLabel = new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "long",
    day: "numeric",
    weekday: "long",
  }).format(new Date(date));
  const ready = snapshot?.ready && !loadError;
  const navigation: { id: Page; label: string; Icon: typeof ListTodo }[] = [
    { id: "tasks", label: "我的任务", Icon: ListTodo },
    { id: "history", label: "历史记录", Icon: HistoryIcon },
    { id: "settings", label: "桌面提醒", Icon: Monitor },
  ];

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand" data-tauri-drag-region>
          <span className="brand-mark" data-tauri-drag-region>
            <Check size={21} strokeWidth={2.5} />
          </span>
          <span data-tauri-drag-region>
            Reminders<span style={{ color: "#a6c3aa" }}>.</span>
          </span>
        </div>
        <div className="sidebar-caption">我的空间</div>
        <nav className="navigation" aria-label="主要导航">
          {navigation.map(({ id, label, Icon }) => (
            <button
              key={id}
              aria-label={label}
              title={label}
              className={`nav-item ${page === id ? "active" : ""}`}
              aria-current={page === id ? "page" : undefined}
              onClick={() => setPage(id)}
            >
              <Icon size={17} strokeWidth={1.6} />
              <span>{label}</span>
              {id === "tasks" && <span className="nav-count">{pending}</span>}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-note">
            <Sprout size={24} strokeWidth={1.2} />
            <strong>少一点惦记，多一点生活。</strong>你不必记住所有事情。
            <br />
            一步一步，也很好。
          </div>
          <button
            className="exit-button"
            disabled={backend.isDemo || busy}
            title={
              backend.isDemo
                ? "浏览器演示不能退出桌面应用"
                : "退出应用并关闭桌面提醒"
            }
            onClick={() =>
              setConfirmation({
                title: "结束今天的陪伴？",
                description:
                  "退出后，桌面提醒也会关闭。下次启动时，任务和历史记录仍会保留。",
                label: "退出应用",
                action: () => backend.exit(),
              })
            }
          >
            <LogOut size={15} />
            <span>退出应用</span>
          </button>
        </div>
      </aside>
      <div className="workspace">
        <header className="titlebar" data-tauri-drag-region>
          <span data-tauri-drag-region>一点秩序，一点从容</span>
          <div className="window-controls">
            <button
              className="window-button"
              disabled={backend.isDemo}
              title={backend.isDemo ? "仅桌面应用可收起到托盘" : "最小化到托盘"}
              aria-label="最小化到托盘"
              onClick={hide}
            >
              <Minus size={14} />
            </button>
            <button
              className="window-button close"
              disabled={backend.isDemo}
              title={
                backend.isDemo
                  ? "仅桌面应用可收起到托盘"
                  : "关闭编辑器，保留桌面提醒"
              }
              aria-label="关闭到托盘"
              onClick={hide}
            >
              <X size={14} />
            </button>
          </div>
        </header>
        <main className="main">
          <div
            className="eyebrow"
            style={{ marginBottom: 24, fontSize: 10, letterSpacing: ".3px" }}
          >
            {dateLabel}
          </div>
          {backend.isDemo && (
            <div className="demo-banner">
              <CloudOff size={14} />
              <span>
                浏览器演示 ·
                使用示例数据，操作仅保留在当前页面，不会保存到桌面应用；不模拟周期计算。
              </span>
            </div>
          )}
          {!snapshot && !loadError && (
            <div className="loading">
              <Loader2
                size={22}
                className="spin"
                style={{ marginBottom: 12 }}
              />
              <p>正在整理你的小事…</p>
            </div>
          )}
          {(loadError || (snapshot && !snapshot.ready)) && (
            <section className="error-state" role="alert">
              <AlertCircle
                size={26}
                style={{ color: "#b88867", marginBottom: 15 }}
              />
              <h2>暂时无法读取或保存任务</h2>
              <p>
                {loadError ||
                  snapshot?.error ||
                  "数据尚未就绪。为保护已有数据，请先解决读取错误。"}
              </p>
              <div style={{ display: "flex", gap: 10 }}>
                <button
                  className="secondary"
                  disabled={busy}
                  onClick={() => {
                    void request("snapshot").catch(notifyError);
                  }}
                >
                  重新读取
                </button>
                {snapshot?.canRestoreBackup && (
                  <button
                    className="primary"
                    disabled={busy}
                    onClick={() =>
                      setConfirmation({
                        title: "恢复上一次有效备份？",
                        description:
                          "当前数据文件将由备份替换。备份之后的修改可能无法保留，请确认后继续。",
                        label: "确认恢复备份",
                        action: async () => {
                          await request("restoreBackup");
                        },
                      })
                    }
                  >
                    从备份恢复
                  </button>
                )}
              </div>
            </section>
          )}
          {ready && snapshot && (
            <>
              {page === "tasks" && (
                <TaskBoard
                  snapshot={snapshot}
                  busy={busy}
                  onEdit={(task, template) => setEditor({ task, template })}
                  onAction={action}
                  onArchive={askArchive}
                />
              )}
              {page === "history" && <History snapshot={snapshot} />}
              {page === "settings" && (
                <OverlaySettings
                  isDemo={backend.isDemo}
                  snapshot={snapshot}
                  busy={busy}
                  onSave={(settings) => request("updateSettings", { settings })}
                />
              )}
            </>
          )}
          <footer className="footer">
            <span>
              <ShieldCheck size={11} />
              离线使用 · 数据留在你的设备
            </span>
            <span>
              <CircleHelp size={11} />
              {snapshot?.timeZoneLabel || "系统本地时间"}
              {snapshot?.timeZoneId ? ` · ${snapshot.timeZoneId}` : ""}
            </span>
          </footer>
        </main>
      </div>
      {editor && ready && (
        <TaskEditor
          task={editor.task}
          template={editor.template}
          order={
            Math.max(
              -1,
              ...(snapshot?.definitions.map((task) => task.order) ?? []),
            ) + 1
          }
          onClose={closeEditor}
          onSave={async (task) => {
            await request("saveTask", { task });
            setEditor(null);
            setToast({
              text: backend.isDemo
                ? "已更新演示，未保存到桌面应用"
                : "这件小事，记下了。",
            });
          }}
        />
      )}
      {confirmation && (
        <div className="modal-backdrop confirm-backdrop">
          <section
            className="confirm-dialog"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="confirm-title"
            aria-describedby="confirm-description"
          >
            <h2 id="confirm-title">{confirmation.title}</h2>
            <p id="confirm-description">{confirmation.description}</p>
            <div className="confirm-actions">
              <button
                className="secondary"
                disabled={busy}
                onClick={() => setConfirmation(null)}
              >
                取消
              </button>
              <button
                className="primary"
                disabled={busy}
                onClick={() => void runConfirmation()}
              >
                {busy && <Loader2 size={13} className="spin" />}
                {confirmation.label}
              </button>
            </div>
          </section>
        </div>
      )}
      {toast && (
        <div
          className={`toast ${toast.error ? "error" : ""}`}
          role={toast.error ? "alert" : "status"}
        >
          {toast.error ? <AlertCircle size={15} /> : <Leaf size={15} />}
          <span>{toast.text}</span>
          <button aria-label="关闭提示" onClick={() => setToast(null)}>
            <X size={13} />
          </button>
        </div>
      )}
    </div>
  );
}
