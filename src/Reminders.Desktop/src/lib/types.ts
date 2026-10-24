export type ScheduleKind = "Daily" | "Weekly" | "EveryNDays" | "Once";
export type Weekday =
  | "Sunday"
  | "Monday"
  | "Tuesday"
  | "Wednesday"
  | "Thursday"
  | "Friday"
  | "Saturday";
export interface Schedule {
  kind: ScheduleKind;
  resetTime: string;
  weekday: Weekday;
  everyDays: number;
  anchorDate: string;
  dueDate: string | null;
  dueTime: string | null;
}
export interface TaskDefinition {
  id: string;
  title: string;
  schedule: Schedule;
  revision: number;
  order: number;
  enabled: boolean;
  archived: boolean;
  createdAt: string;
}
export interface TaskStatus {
  task: TaskDefinition;
  period: { key: string; start: string | null; end: string | null } | null;
  isCompleted: boolean;
  isOverdue: boolean;
  isAvailable: boolean;
}
export interface OverlaySettings {
  left: number;
  top: number;
  width: number;
  height: number;
  fontSize: number;
  opacity: number;
  topmost: boolean;
  visible: boolean;
}
export interface CompletionEvent {
  id: string;
  taskId: string;
  revision: number;
  periodKey: string;
  action: "Complete" | "Undo";
  occurredAt: string;
  title: string;
  periodStart: string | null;
  periodEnd: string | null;
  timeZoneId: string;
}
export interface Snapshot {
  sequence: number;
  ready: boolean;
  error?: string;
  canRestoreBackup?: boolean;
  tasks: TaskStatus[];
  definitions: TaskDefinition[];
  events: CompletionEvent[];
  overlay: OverlaySettings;
  timeZoneId: string;
  timeZoneLabel: string;
  now: string;
}
export type BackendMethod =
  | "snapshot"
  | "saveTask"
  | "complete"
  | "undo"
  | "setEnabled"
  | "archive"
  | "move"
  | "updateSettings"
  | "restoreBackup";
export interface BackendApi {
  isDemo: boolean;
  request: (
    method: BackendMethod,
    payload?: Record<string, unknown>,
  ) => Promise<Snapshot>;
  subscribe: (
    callback: (state: Snapshot) => void,
    onError?: (error: string) => void,
  ) => Promise<() => void>;
  hide: () => Promise<void>;
  exit: () => Promise<void>;
}
