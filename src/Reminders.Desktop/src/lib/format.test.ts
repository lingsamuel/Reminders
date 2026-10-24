import { expect, it } from "vitest";
import { localTime } from "./format";

it("历史时间可显示年份，同时保留任务卡片的简短格式", () => {
  const value = "2025-10-24T09:15:00+08:00";
  expect(localTime(value, true)).toContain("2025");
  expect(localTime(value)).not.toContain("2025");
  expect(localTime(null, true)).toBe("—");
});
