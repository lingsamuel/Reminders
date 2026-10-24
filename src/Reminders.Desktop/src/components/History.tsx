import { useState } from "react";
import { Check, History as HistoryIcon, Undo2 } from "lucide-react";
import type { Snapshot } from "../lib/types";
import { localTime } from "../lib/format";

export function History({ snapshot }: { snapshot: Snapshot }) {
  const [taskId, setTaskId] = useState("");
  const taskNames = new Map(
    snapshot.definitions.map((task) => [task.id, task.title]),
  );
  // 历史中的任务可能已归档，直接使用事件快照补齐筛选项。
  snapshot.events.forEach((event) => {
    if (!taskNames.has(event.taskId)) taskNames.set(event.taskId, event.title);
  });
  const events = [...snapshot.events]
    .filter((event) => !taskId || event.taskId === taskId)
    .sort((a, b) => Date.parse(b.occurredAt) - Date.parse(a.occurredAt));
  return (
    <>
      <div className="page-heading">
        <div>
          <div className="eyebrow">LITTLE WINS, ALL REMEMBERED</div>
          <h1>每一份完成，都算数。</h1>
          <p className="subtitle">完成与撤销的足迹，都好好留在这里。</p>
        </div>
      </div>
      <div className="history-filter">
        <select
          aria-label="按任务筛选历史"
          value={taskId}
          onChange={(event) => setTaskId(event.target.value)}
        >
          <option value="">全部任务</option>
          {[...taskNames].map(([id, title]) => (
            <option key={id} value={id}>
              {title}
            </option>
          ))}
        </select>
        <span style={{ color: "#65775f", fontSize: 11 }}>
          {events.length} 条记录
        </span>
      </div>
      {events.length ? (
        <div className="history-list">
          {events.map((event) => (
            <article className="history-item" key={event.id}>
              <div
                className={`history-icon ${event.action === "Undo" ? "undo" : ""}`}
              >
                {event.action === "Complete" ? (
                  <Check size={15} />
                ) : (
                  <Undo2 size={14} />
                )}
              </div>
              <div>
                <p className="history-action">
                  {event.action === "Complete" ? "完成了任务" : "撤销了完成"}
                </p>
                <h3 className="history-title">{event.title}</h3>
                <p className="history-meta">
                  {event.periodStart
                    ? `周期 ${localTime(event.periodStart, true)} → ${localTime(event.periodEnd, true)}`
                    : event.periodEnd
                      ? `截止 ${localTime(event.periodEnd, true)}`
                      : "一次性任务 / 演示记录"}
                  <br />
                  {event.timeZoneId}
                </p>
              </div>
              <time className="history-time" dateTime={event.occurredAt}>
                {localTime(event.occurredAt, true)}
              </time>
            </article>
          ))}
        </div>
      ) : (
        <div className="empty-state">
          <div className="empty-icon">
            <HistoryIcon size={24} />
          </div>
          <h3>故事从下一次完成开始</h3>
          <p>完成任务后，这里会留下你的每一步。</p>
        </div>
      )}
    </>
  );
}
