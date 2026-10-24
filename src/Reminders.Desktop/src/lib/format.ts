import type { Schedule, TaskStatus, Weekday } from "./types";

export const weekdays: Record<Weekday, string> = {
  Monday: "周一",
  Tuesday: "周二",
  Wednesday: "周三",
  Thursday: "周四",
  Friday: "周五",
  Saturday: "周六",
  Sunday: "周日",
};
export const kindLabels = {
  Daily: "每日",
  Weekly: "每周",
  EveryNDays: "每 N 天",
  Once: "一次性",
} as const;
export function scheduleText(schedule: Schedule): string {
  const time = schedule.resetTime.slice(0, 5);
  switch (schedule.kind) {
    case "Daily":
      return `每日 ${time} 重置`;
    case "Weekly":
      return `每${weekdays[schedule.weekday]} ${time} 重置`;
    case "EveryNDays":
      return `每 ${schedule.everyDays} 天 · ${time} 重置`;
    case "Once":
      return `${schedule.dueDate} ${schedule.dueTime ? schedule.dueTime.slice(0, 5) : "当天结束"} 截止`;
  }
}
export function statusText(status: TaskStatus): string {
  if (!status.task.enabled) return "已停用";
  if (!status.isAvailable) return "尚未开始";
  if (status.isCompleted) return "已完成";
  return status.isOverdue ? "已逾期" : "待完成";
}
export function localTime(
  value: string | null | undefined,
  includeYear = false,
): string {
  if (!value) return "—";
  return new Intl.DateTimeFormat("zh-CN", {
    ...(includeYear ? { year: "numeric" as const } : {}),
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(new Date(value));
}
export function errorText(error: unknown): string {
  return error instanceof Error
    ? error.message
    : typeof error === "string"
      ? error
      : "操作失败，请稍后重试";
}
export function today(): string {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
