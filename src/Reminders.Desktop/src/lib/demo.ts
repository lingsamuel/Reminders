import type {
  BackendMethod,
  Schedule,
  Snapshot,
  TaskDefinition,
  TaskStatus,
} from "./types";

const sampleSchedule: Schedule = {
  kind: "Daily",
  resetTime: "05:00:00",
  weekday: "Thursday",
  everyDays: 2,
  anchorDate: "2026-10-24",
  dueDate: null,
  dueTime: null,
};
const sampleTasks: TaskDefinition[] = [
  {
    id: "demo-epic",
    title: "领取 Epic 本周免费游戏",
    schedule: { ...sampleSchedule, kind: "Weekly", resetTime: "00:00:00" },
    revision: 1,
    order: 0,
    enabled: true,
    archived: false,
    createdAt: "2026-10-24T08:00:00+08:00",
  },
  {
    id: "demo-daily",
    title: "完成今日游戏日常",
    schedule: sampleSchedule,
    revision: 1,
    order: 1,
    enabled: true,
    archived: false,
    createdAt: "2026-10-24T08:00:00+08:00",
  },
  {
    id: "demo-body",
    title: "检查体力，留一点时间给自己",
    schedule: { ...sampleSchedule, kind: "EveryNDays" },
    revision: 1,
    order: 2,
    enabled: true,
    archived: false,
    createdAt: "2026-10-24T08:00:00+08:00",
  },
  {
    id: "demo-once",
    title: "整理这个月的收藏清单",
    schedule: { ...sampleSchedule, kind: "Once", dueDate: "2026-10-31" },
    revision: 1,
    order: 3,
    enabled: true,
    archived: false,
    createdAt: "2026-10-24T08:00:00+08:00",
  },
];

// 演示数据只用于浏览器视觉预览；不模拟 Core 周期、版本或持久化语义。
export function createDemoRequest() {
  let state: Snapshot = {
    sequence: 0,
    ready: true,
    definitions: sampleTasks,
    tasks: sampleTasks.map((task, index) => ({
      task,
      period: null,
      isCompleted: index === 1,
      isOverdue: false,
      isAvailable: true,
    })),
    events: [
      {
        id: "demo-event",
        taskId: "demo-daily",
        title: "完成今日游戏日常",
        revision: 1,
        periodKey: "preview",
        action: "Complete",
        occurredAt: "2026-10-24T09:15:00+08:00",
        periodStart: null,
        periodEnd: null,
        timeZoneId: "Asia/Hong_Kong",
      },
    ],
    overlay: {
      left: 80,
      top: 100,
      width: 340,
      height: 260,
      fontSize: 16,
      opacity: 0.88,
      topmost: true,
      visible: true,
    },
    timeZoneId: "Asia/Hong_Kong",
    timeZoneLabel: "系统本地时间",
    now: new Date().toISOString(),
  };
  return async (
    method: BackendMethod,
    payload: Record<string, unknown> = {},
  ): Promise<Snapshot> => {
    const next = structuredClone(state);
    const index = next.tasks.findIndex((item) => item.task.id === payload.id);
    const status = next.tasks[index];
    if (method === "saveTask") {
      const task = structuredClone(payload.task as TaskDefinition);
      const previous = next.tasks.find((item) => item.task.id === task.id);
      const item: TaskStatus = previous
        ? { ...previous, task }
        : {
            task,
            period: null,
            isCompleted: false,
            isOverdue: false,
            isAvailable: true,
          };
      next.tasks = [
        ...next.tasks.filter((entry) => entry.task.id !== task.id),
        item,
      ].sort((a, b) => a.task.order - b.task.order);
    } else if ((method === "complete" || method === "undo") && status) {
      status.isCompleted = method === "complete";
      next.events.unshift({
        id: crypto.randomUUID(),
        taskId: status.task.id,
        title: status.task.title,
        revision: status.task.revision,
        periodKey: "preview",
        action: method === "complete" ? "Complete" : "Undo",
        occurredAt: new Date().toISOString(),
        periodStart: null,
        periodEnd: null,
        timeZoneId: next.timeZoneId,
      });
    } else if (method === "setEnabled" && status)
      status.task.enabled = Boolean(payload.enabled);
    else if (method === "archive" && status) status.task.archived = true;
    else if (method === "move" && status) {
      const target = Math.max(
        0,
        Math.min(next.tasks.length - 1, index + Number(payload.delta)),
      );
      next.tasks.splice(index, 1);
      next.tasks.splice(target, 0, status);
      next.tasks.forEach((item, order) => {
        item.task.order = order;
      });
    } else if (method === "updateSettings")
      next.overlay = structuredClone(payload.settings as Snapshot["overlay"]);
    next.definitions = next.tasks.map((item) => item.task);
    next.tasks = next.tasks.filter((item) => !item.task.archived);
    if (method !== "snapshot") next.sequence += 1;
    state = next;
    return structuredClone(state);
  };
}
