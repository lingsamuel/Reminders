import { useEffect, useRef, useState } from "react";
import {
  Archive,
  ArrowDown,
  ArrowUp,
  CalendarDays,
  CalendarClock,
  Check,
  CheckCheck,
  Clock3,
  Edit3,
  Gamepad2,
  Leaf,
  MoreHorizontal,
  Pause,
  Play,
  Plus,
  Repeat2,
  Search,
  Sparkles,
} from "lucide-react";
import type {
  BackendMethod,
  Schedule,
  Snapshot,
  TaskDefinition,
  TaskStatus,
} from "../lib/types";
import {
  kindLabels,
  localTime,
  scheduleText,
  statusText,
  today,
} from "../lib/format";

export interface TaskTemplate {
  title: string;
  schedule: Partial<Schedule>;
}
interface Props {
  snapshot: Snapshot;
  busy: boolean;
  onEdit: (task?: TaskDefinition, template?: TaskTemplate) => void;
  onAction: (method: BackendMethod, payload: Record<string, unknown>) => void;
  onArchive: (task: TaskDefinition) => void;
}
const templates = [
  {
    label: "Epic 免费游戏",
    description: "每周四 · 00:00",
    Icon: Gamepad2,
    title: "领取 Epic 本周免费游戏",
    schedule: { kind: "Weekly", weekday: "Thursday", resetTime: "00:00:00" },
  },
  {
    label: "每日 05:00",
    description: "新的一天，从这里开始",
    Icon: Clock3,
    title: "完成今日游戏日常",
    schedule: { kind: "Daily", resetTime: "05:00:00" },
  },
  {
    label: "每日 04:00",
    description: "给另一份日常留个位置",
    Icon: CalendarDays,
    title: "完成另一份游戏日常",
    schedule: { kind: "Daily", resetTime: "04:00:00" },
  },
  {
    label: "每两天一次",
    description: "固定周期 · 从今天开始",
    Icon: Repeat2,
    title: "检查体力",
    schedule: { kind: "EveryNDays", everyDays: 2 },
  },
] as const;

function TaskCard({
  status,
  busy,
  onEdit,
  onAction,
  onArchive,
}: Omit<Props, "snapshot"> & { status: TaskStatus }) {
  const [menu, setMenu] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const { task } = status;
  const completed = status.isCompleted;
  useEffect(() => {
    if (!menu) return;
    const close = (event: MouseEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) setMenu(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMenu(false);
    };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", escape);
    };
  }, [menu]);
  const action = (method: BackendMethod, payload: Record<string, unknown>) => {
    setMenu(false);
    onAction(method, payload);
  };
  return (
    <article className={`task-card ${completed ? "completed" : ""}`}>
      <input
        className="task-check"
        type="checkbox"
        aria-label={`${completed ? "撤销完成" : "完成"} ${task.title}`}
        checked={completed}
        disabled={busy || !task.enabled || !status.isAvailable}
        onChange={() =>
          onAction(completed ? "undo" : "complete", { id: task.id })
        }
      />
      <div className="task-body">
        <div className="task-title-row">
          <h3 className="task-title">{task.title}</h3>
          <span className={`pill ${task.schedule.kind}`}>
            {kindLabels[task.schedule.kind]}
          </span>
          {status.isOverdue && !completed && (
            <span className="pill overdue">已逾期</span>
          )}
        </div>
        <div className="task-metadata">
          {task.schedule.kind === "Once" ? (
            <CalendarClock size={11} />
          ) : (
            <Repeat2 size={11} />
          )}
          <span>{scheduleText(task.schedule)}</span>
          {status.period?.end && task.schedule.kind !== "Once" && (
            <>
              <span className="metadata-divider">·</span>
              <span>下次 {localTime(status.period.end)}</span>
            </>
          )}
          {task.schedule.kind === "EveryNDays" && (
            <>
              <span className="metadata-divider">·</span>
              <span>起始 {task.schedule.anchorDate}</span>
            </>
          )}
        </div>
      </div>
      <div className="task-actions">
        <span
          className={`task-state ${!task.enabled || !status.isAvailable ? "muted" : status.isOverdue ? "warning" : ""}`}
        >
          {completed ? (
            <CheckCheck size={12} />
          ) : (
            <span
              style={{
                width: 4,
                height: 4,
                borderRadius: "50%",
                background: "currentColor",
              }}
            />
          )}
          {statusText(status)}
        </span>
        <div className="card-separator" />
        <button
          className="icon-button edit-action"
          title="编辑任务"
          aria-label={`编辑 ${task.title}`}
          disabled={busy}
          onClick={() => onEdit(task)}
        >
          <Edit3 size={14} />
        </button>
        <div className="task-menu-wrap" ref={menuRef}>
          <button
            className="icon-button"
            aria-label={`更多操作 ${task.title}`}
            aria-haspopup="menu"
            aria-expanded={menu}
            disabled={busy}
            onClick={() => setMenu(!menu)}
          >
            <MoreHorizontal size={17} />
          </button>
          {menu && (
            <div className="dropdown" role="menu">
              <button
                role="menuitem"
                onClick={() => {
                  setMenu(false);
                  onEdit(task);
                }}
              >
                <Edit3 size={13} />
                编辑任务
              </button>
              <button
                role="menuitem"
                onClick={() =>
                  action("setEnabled", { id: task.id, enabled: !task.enabled })
                }
              >
                {task.enabled ? <Pause size={13} /> : <Play size={13} />}
                {task.enabled ? "停用任务" : "启用任务"}
              </button>
              <button
                role="menuitem"
                onClick={() => action("move", { id: task.id, delta: -1 })}
              >
                <ArrowUp size={13} />
                向上移动
              </button>
              <button
                role="menuitem"
                onClick={() => action("move", { id: task.id, delta: 1 })}
              >
                <ArrowDown size={13} />
                向下移动
              </button>
              <button
                role="menuitem"
                className="danger"
                onClick={() => {
                  setMenu(false);
                  onArchive(task);
                }}
              >
                <Archive size={13} />
                归档任务
              </button>
            </div>
          )}
        </div>
      </div>
    </article>
  );
}

export function TaskBoard(props: Props) {
  const { snapshot, onEdit } = props;
  const [filter, setFilter] = useState<"all" | "repeat" | "once">("all");
  const [query, setQuery] = useState("");
  const active = snapshot.tasks.filter((status) => !status.task.archived);
  const filtered = active.filter(
    ({ task }) =>
      (filter === "all" ||
        (filter === "once"
          ? task.schedule.kind === "Once"
          : task.schedule.kind !== "Once")) &&
      task.title.toLocaleLowerCase().includes(query.toLocaleLowerCase()),
  );
  const pending = filtered.filter((status) => !status.isCompleted);
  const completed = filtered.filter((status) => status.isCompleted);
  const todoCount = active.filter(
    (status) =>
      status.task.enabled && status.isAvailable && !status.isCompleted,
  ).length;
  const completedCount = active.filter((status) => status.isCompleted).length;
  return (
    <>
      <div className="page-heading">
        <div>
          <div className="eyebrow">YOUR SPACE, YOUR PACE</div>
          <h1>今天，也慢慢来。</h1>
          <p className="subtitle">
            把琐事交给 Reminders，把注意力留给喜欢的事。
          </p>
        </div>
        <button
          className="primary heading-action"
          disabled={props.busy}
          onClick={() => onEdit()}
        >
          <Plus size={15} />
          新建任务
        </button>
      </div>
      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-icon">
            <Leaf size={20} strokeWidth={1.6} />
          </div>
          <div>
            <div className="stat-label">待完成</div>
            <div className="stat-value">
              {todoCount}
              <span className="stat-unit">件小事</span>
            </div>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon green">
            <CheckCheck size={20} strokeWidth={1.6} />
          </div>
          <div>
            <div className="stat-label">本周期已完成</div>
            <div className="stat-value">
              {completedCount}
              <span className="stat-unit">份轻松</span>
            </div>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon sand">
            <CalendarDays size={20} strokeWidth={1.6} />
          </div>
          <div>
            <div className="stat-label">全部任务</div>
            <div className="stat-value">
              {active.length}
              <span className="stat-unit">个安排</span>
            </div>
          </div>
        </div>
      </div>
      <div className="toolbar">
        <div className="tabs" role="tablist" aria-label="任务类型">
          <button
            role="tab"
            aria-selected={filter === "all"}
            className={`tab ${filter === "all" ? "active" : ""}`}
            onClick={() => setFilter("all")}
          >
            全部任务
          </button>
          <button
            role="tab"
            aria-selected={filter === "repeat"}
            className={`tab ${filter === "repeat" ? "active" : ""}`}
            onClick={() => setFilter("repeat")}
          >
            重复任务
          </button>
          <button
            role="tab"
            aria-selected={filter === "once"}
            className={`tab ${filter === "once" ? "active" : ""}`}
            onClick={() => setFilter("once")}
          >
            一次性
          </button>
        </div>
        <label className="search-box">
          <Search size={13} />
          <input
            aria-label="搜索任务"
            placeholder="搜索任务…"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>
      </div>
      {pending.length > 0 && (
        <>
          <div className="section-label">
            留给今天的小事<span>{pending.length} 个任务</span>
          </div>
          <div className="tasks-list">
            {pending.map((status) => (
              <TaskCard key={status.task.id} {...props} status={status} />
            ))}
          </div>
        </>
      )}
      {filtered.length === 0 && (
        <div className="empty-state">
          <div className="empty-icon">
            <Leaf size={25} strokeWidth={1.5} />
          </div>
          <h3>
            {query
              ? "没有找到这件小事"
              : active.length
                ? "这里还没有任务"
                : "为生活留一点轻松"}
          </h3>
          <p>
            {query
              ? "试试换个关键词。"
              : "添加第一个任务，或从下面的模板开始。"}
          </p>
          {!query && (
            <button className="secondary" onClick={() => onEdit()}>
              <Plus size={13} />
              添加任务
            </button>
          )}
        </div>
      )}
      {pending.length === 0 && filtered.length > 0 && (
        <div className="empty-state">
          <div className="empty-icon">
            <Check size={26} />
          </div>
          <h3>这些小事，都做好了。</h3>
          <p>享受这一刻的轻松。下个周期，我们再见。</p>
        </div>
      )}
      {completed.length > 0 && (
        <>
          <div className="completed-label">已经完成 · {completed.length}</div>
          <div className="tasks-list">
            {completed.map((status) => (
              <TaskCard key={status.task.id} {...props} status={status} />
            ))}
          </div>
        </>
      )}
      <section className="templates-section">
        <h2 className="templates-heading">
          <Sparkles size={14} />
          从一个小模板开始
        </h2>
        <div className="template-grid">
          {templates.map(({ label, description, Icon, title, schedule }) => (
            <button
              className="template"
              key={label}
              disabled={props.busy}
              onClick={() =>
                onEdit(undefined, {
                  title,
                  schedule: { ...schedule, anchorDate: today() },
                })
              }
            >
              <span className="template-icon">
                <Icon size={14} />
              </span>
              <Plus className="template-add" size={12} />
              <strong>{label}</strong>
              <small>{description}</small>
            </button>
          ))}
        </div>
      </section>
    </>
  );
}
