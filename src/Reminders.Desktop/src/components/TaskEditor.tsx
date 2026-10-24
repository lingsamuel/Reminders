import { useEffect, useRef, useState } from "react";
import {
  CalendarDays,
  CalendarClock,
  Check,
  Clock3,
  Info,
  Loader2,
  Repeat2,
  X,
} from "lucide-react";
import type {
  Schedule,
  ScheduleKind,
  TaskDefinition,
  Weekday,
} from "../lib/types";
import { errorText, kindLabels, today, weekdays } from "../lib/format";

interface Props {
  task?: TaskDefinition;
  template?: { title: string; schedule: Partial<Schedule> };
  order?: number;
  onSave: (task: TaskDefinition) => Promise<void> | void;
  onClose: () => void;
}
const kindIcons = {
  Daily: Clock3,
  Weekly: CalendarDays,
  EveryNDays: Repeat2,
  Once: CalendarClock,
};

export function TaskEditor({
  task,
  template,
  order = 0,
  onSave,
  onClose,
}: Props) {
  const initial: Schedule = task?.schedule ?? {
    kind: "Daily",
    resetTime: "05:00:00",
    weekday: "Thursday",
    everyDays: 2,
    anchorDate: today(),
    dueDate: null,
    dueTime: null,
    ...template?.schedule,
  };
  const [title, setTitle] = useState(task?.title ?? template?.title ?? "");
  // 超时可能发生在已落盘之后，重试必须沿用同一身份，避免重复创建。
  const [draftId] = useState(() => task?.id ?? crypto.randomUUID());
  const [schedule, setSchedule] = useState<Schedule>(initial);
  const [days, setDays] = useState(String(initial.everyDays));
  const [enabled, setEnabled] = useState(task?.enabled ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const panel = useRef<HTMLDivElement>(null);
  const titleInput = useRef<HTMLInputElement>(null);
  const update = (patch: Partial<Schedule>) =>
    setSchedule((previous) => ({ ...previous, ...patch }));

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    titleInput.current?.focus();
    return () => previous?.focus();
  }, []);
  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !saving) onClose();
      if (event.key !== "Tab") return;
      const elements = panel.current?.querySelectorAll<HTMLElement>(
        "button:not(:disabled), input:not(:disabled), select:not(:disabled)",
      );
      if (!elements?.length) return;
      const first = elements[0],
        last = elements[elements.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", handleKey);
    return () => document.removeEventListener("keydown", handleKey);
  }, [onClose, saving]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!title.trim()) {
      setError("请填写任务名称");
      titleInput.current?.focus();
      return;
    }
    if (schedule.kind === "Once" && !schedule.dueDate) {
      setError("请选择截止日期");
      return;
    }
    if (
      schedule.kind === "EveryNDays" &&
      (!Number.isSafeInteger(Number(days)) || Number(days) <= 0)
    ) {
      setError("间隔天数必须是正整数");
      return;
    }
    if (schedule.kind === "EveryNDays" && !schedule.anchorDate) {
      setError("请选择起始日期");
      return;
    }
    if (schedule.kind !== "Once" && !schedule.resetTime) {
      setError("请选择重置时间");
      return;
    }
    setSaving(true);
    setError("");
    try {
      await onSave({
        id: draftId,
        title: title.trim(),
        schedule: { ...schedule, everyDays: Number(days) || initial.everyDays },
        revision: task?.revision ?? 1,
        order: task?.order ?? order,
        enabled,
        archived: task?.archived ?? false,
        createdAt: task?.createdAt ?? new Date().toISOString(),
      });
    } catch (failure) {
      setError(errorText(failure));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !saving) onClose();
      }}
    >
      <div
        ref={panel}
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="editor-title"
      >
        <div className="drawer-head">
          <div>
            <h2 id="editor-title">{task ? "编辑任务" : "添加一个小目标"}</h2>
            <p>把值得记住的事，放在刚刚好的时间。</p>
          </div>
          <button
            className="icon-button"
            aria-label="关闭编辑"
            disabled={saving}
            onClick={onClose}
          >
            <X size={19} />
          </button>
        </div>
        <form className="drawer-form" onSubmit={submit} noValidate>
          <label className="field">
            <span>任务名称</span>
            <input
              ref={titleInput}
              aria-label="任务名称"
              placeholder="想要记住什么？"
              value={title}
              maxLength={120}
              disabled={saving}
              onChange={(event) => setTitle(event.target.value)}
            />
          </label>
          <div>
            <span className="field-label" id="schedule-label">
              重复方式
            </span>
            <div
              role="radiogroup"
              aria-labelledby="schedule-label"
              className="type-grid"
            >
              {(Object.keys(kindLabels) as ScheduleKind[]).map((kind) => {
                const Icon = kindIcons[kind];
                return (
                  <button
                    key={kind}
                    type="button"
                    role="radio"
                    aria-checked={schedule.kind === kind}
                    disabled={saving}
                    className={`type-option ${schedule.kind === kind ? "selected" : ""}`}
                    onClick={() => update({ kind })}
                  >
                    <Icon size={18} />
                    {kindLabels[kind]}
                  </button>
                );
              })}
            </div>
          </div>
          {schedule.kind === "Weekly" && (
            <label className="field">
              <span>重置日</span>
              <select
                aria-label="重置日"
                disabled={saving}
                value={schedule.weekday}
                onChange={(event) =>
                  update({ weekday: event.target.value as Weekday })
                }
              >
                {Object.entries(weekdays).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
          )}
          {schedule.kind === "EveryNDays" && (
            <div className="form-grid">
              <label className="field">
                <span>间隔天数</span>
                <input
                  aria-label="间隔天数"
                  disabled={saving}
                  type="number"
                  min="1"
                  step="1"
                  value={days}
                  onChange={(event) => setDays(event.target.value)}
                />
              </label>
              <label className="field">
                <span>起始日期</span>
                <input
                  aria-label="起始日期"
                  disabled={saving}
                  type="date"
                  value={schedule.anchorDate}
                  onChange={(event) =>
                    update({ anchorDate: event.target.value })
                  }
                />
              </label>
            </div>
          )}
          {schedule.kind === "Once" ? (
            <div className="form-grid">
              <label className="field">
                <span>截止日期</span>
                <input
                  aria-label="截止日期"
                  disabled={saving}
                  type="date"
                  value={schedule.dueDate ?? ""}
                  onChange={(event) =>
                    update({ dueDate: event.target.value || null })
                  }
                />
              </label>
              <label className="field">
                <span>截止时间（可选）</span>
                <input
                  aria-label="截止时间（可选）"
                  disabled={saving}
                  type="time"
                  value={schedule.dueTime?.slice(0, 5) ?? ""}
                  onChange={(event) =>
                    update({
                      dueTime: event.target.value
                        ? `${event.target.value}:00`
                        : null,
                    })
                  }
                />
              </label>
            </div>
          ) : (
            <label className="field">
              <span>重置时间</span>
              <input
                aria-label="重置时间"
                disabled={saving}
                type="time"
                value={schedule.resetTime.slice(0, 5)}
                onChange={(event) =>
                  update({
                    resetTime: event.target.value
                      ? `${event.target.value}:00`
                      : "",
                  })
                }
              />
              <small>使用系统本地时间。每个任务独立重置。</small>
            </label>
          )}
          <div className="form-note">
            <Info size={15} />
            <span>
              {schedule.kind === "Once"
                ? "未选时间时，以截止日期当天结束为界。逾期任务会一直保留，直到完成、停用或归档。"
                : schedule.kind === "EveryNDays"
                  ? "以起始日期为锚点，按固定日历天循环。完成时间不会改变下次重置时间。"
                  : "本周期完成后，任务会在下一个重置时间自动出现。错过的周期不会累积。"}
            </span>
          </div>
          {task && (
            <div className="form-note warning">
              <Info size={15} />
              <span>
                修改重复规则或切换任务类型时，当前周期可能重新开始；仅修改名称或一次性截止时间不会重置完成状态。历史记录会保留。
              </span>
            </div>
          )}
          <div className="switch-line">
            <div>
              <strong>启用这个任务</strong>
              <p>停用后从桌面隐藏，保留当前完成状态。</p>
            </div>
            <button
              aria-label="启用这个任务"
              role="switch"
              aria-checked={enabled}
              type="button"
              disabled={saving}
              className={`switch ${enabled ? "on" : ""}`}
              onClick={() => setEnabled(!enabled)}
            />
          </div>
          {error && (
            <div role="alert" className="form-error">
              {error}
            </div>
          )}
          <div className="drawer-footer">
            <button
              type="button"
              className="secondary"
              disabled={saving}
              onClick={onClose}
            >
              取消
            </button>
            <button type="submit" className="primary" disabled={saving}>
              {saving ? (
                <Loader2 size={14} className="spin" />
              ) : (
                <Check size={14} />
              )}
              {saving ? "正在保存…" : task ? "保存更改" : "创建任务"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
