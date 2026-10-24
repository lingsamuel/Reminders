import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App } from "./App";
import { createDemoRequest } from "./lib/demo";
import type { BackendApi, Snapshot } from "./lib/types";

function backend(): BackendApi {
  return {
    isDemo: true,
    request: createDemoRequest(),
    subscribe: async () => () => {},
    hide: async () => {},
    exit: async () => {},
  };
}
describe("编辑器行为", () => {
  it.each([1, 2])(
    "后台退出后，先前挂起的操作响应（sequence=%i）不能恢复写入",
    async (sequence) => {
      const service = backend();
      const initial = { ...(await service.request("snapshot")), sequence: 1 };
      let reportError!: (message: string) => void;
      let resolveRequest!: (state: Snapshot) => void;
      const pending = new Promise<Snapshot>((resolve) => {
        resolveRequest = resolve;
      });
      service.subscribe = async (_callback, onError) => {
        reportError = onError!;
        return () => {};
      };
      service.request = async (method) =>
        method === "snapshot" ? initial : pending;
      render(<App backend={service} />);
      await userEvent.click(
        await screen.findByRole("checkbox", {
          name: "完成 领取 Epic 本周免费游戏",
        }),
      );
      act(() => reportError("后端已退出"));
      await act(async () => resolveRequest({ ...initial, sequence }));
      expect(screen.getByText("后端已退出")).toBeVisible();
      expect(
        screen.queryByRole("button", { name: "新建任务" }),
      ).not.toBeInTheDocument();
    },
  );

  it.each([1, 2])(
    "后台退出后，挂起的初始连接响应（sequence=%i）不能恢复写入",
    async (sequence) => {
      const service = backend();
      const initial = { ...(await service.request("snapshot")), sequence: 1 };
      let reportError!: (message: string) => void;
      let resolveRequest!: (state: Snapshot) => void;
      let started = false;
      const pending = new Promise<Snapshot>((resolve) => {
        resolveRequest = resolve;
      });
      service.subscribe = async (_callback, onError) => {
        reportError = onError!;
        return () => {};
      };
      service.request = () => {
        started = true;
        return pending;
      };
      render(<App backend={service} />);
      await waitFor(() => expect(started).toBe(true));
      act(() => reportError("初始连接期间后端已退出"));
      await act(async () => resolveRequest({ ...initial, sequence }));
      expect(screen.getByText("初始连接期间后端已退出")).toBeVisible();
      expect(
        screen.queryByRole("button", { name: "新建任务" }),
      ).not.toBeInTheDocument();
    },
  );

  it("错误后的订阅状态不会自动恢复写入", async () => {
    const service = backend();
    const initial = { ...(await service.request("snapshot")), sequence: 1 };
    let push!: (state: Snapshot) => void;
    let reportError!: (message: string) => void;
    service.subscribe = async (callback, onError) => {
      push = callback;
      reportError = onError!;
      return () => {};
    };
    service.request = async () => initial;
    render(<App backend={service} />);
    await screen.findByRole("button", { name: "新建任务" });
    act(() => reportError("等待主动重新读取"));
    act(() => push({ ...initial, sequence: 2 }));
    expect(screen.getByText("等待主动重新读取")).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "新建任务" }),
    ).not.toBeInTheDocument();
  });

  it("重新读取期间再次收到后台错误时，重读响应也不能恢复写入", async () => {
    const service = backend();
    const initial = { ...(await service.request("snapshot")), sequence: 1 };
    let reportError!: (message: string) => void;
    let resolveRequest!: (state: Snapshot) => void;
    const pending = new Promise<Snapshot>((resolve) => {
      resolveRequest = resolve;
    });
    let requests = 0;
    service.subscribe = async (_callback, onError) => {
      reportError = onError!;
      return () => {};
    };
    service.request = () =>
      ++requests === 1 ? Promise.resolve(initial) : pending;
    render(<App backend={service} />);
    await screen.findByRole("button", { name: "新建任务" });
    act(() => reportError("第一次后台错误"));
    await userEvent.click(screen.getByRole("button", { name: "重新读取" }));
    act(() => reportError("重读期间再次退出"));
    await act(async () => resolveRequest({ ...initial, sequence: 3 }));
    expect(screen.getByText("重读期间再次退出")).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "新建任务" }),
    ).not.toBeInTheDocument();
  });
  it("旧操作响应不能清除较新的后台错误", async () => {
    const service = backend();
    const initial = { ...(await service.request("snapshot")), sequence: 1 };
    let push!: (state: Snapshot) => void;
    let reportError!: (message: string) => void;
    let resolveRequest!: (state: Snapshot) => void;
    const pending = new Promise<Snapshot>((resolve) => {
      resolveRequest = resolve;
    });
    service.subscribe = async (callback, onError) => {
      push = callback;
      reportError = onError!;
      return () => {};
    };
    service.request = async (method) =>
      method === "snapshot" ? initial : pending;
    render(<App backend={service} />);
    await userEvent.click(
      await screen.findByRole("checkbox", {
        name: "完成 领取 Epic 本周免费游戏",
      }),
    );
    act(() => {
      push({ ...initial, sequence: 2 });
      reportError("较新的后台故障");
    });
    await act(async () => resolveRequest(initial));
    expect(screen.getByText("较新的后台故障")).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "新建任务" }),
    ).not.toBeInTheDocument();
  });

  it("后台错误后显式重读可接受同版本的有效快照", async () => {
    const service = backend();
    let reportError!: (message: string) => void;
    service.subscribe = async (_callback, onError) => {
      reportError = onError!;
      return () => {};
    };
    render(<App backend={service} />);
    await screen.findByRole("button", { name: "新建任务" });
    act(() => reportError("临时后台故障"));
    await userEvent.click(screen.getByRole("button", { name: "重新读取" }));
    expect(
      await screen.findByRole("button", { name: "新建任务" }),
    ).toBeVisible();
    expect(screen.queryByText("临时后台故障")).not.toBeInTheDocument();
  });
  it("较旧的操作响应不能覆盖已收到的新事件快照", async () => {
    const service = backend();
    const initial = { ...(await service.request("snapshot")), sequence: 1 };
    let push!: (state: Snapshot) => void;
    let resolveRequest!: (state: Snapshot) => void;
    const pending = new Promise<Snapshot>((resolve) => {
      resolveRequest = resolve;
    });
    service.subscribe = async (callback) => {
      push = callback;
      return () => {};
    };
    service.request = async (method) =>
      method === "snapshot" ? initial : pending;
    render(<App backend={service} />);
    await userEvent.click(
      await screen.findByRole("checkbox", {
        name: "完成 领取 Epic 本周免费游戏",
      }),
    );
    const newer = {
      ...initial,
      sequence: 2,
      tasks: initial.tasks.map((status) =>
        status.task.id === "demo-epic"
          ? { ...status, isCompleted: true }
          : status,
      ),
    };
    act(() => push(newer));
    expect(
      screen.getByRole("checkbox", { name: "撤销完成 领取 Epic 本周免费游戏" }),
    ).toBeChecked();
    await act(async () => resolveRequest(initial));
    expect(
      screen.getByRole("checkbox", { name: "撤销完成 领取 Epic 本周免费游戏" }),
    ).toBeChecked();
  });

  it("连接时的较旧初始响应不能覆盖先到达的新事件", async () => {
    const service = backend();
    const initial = { ...(await service.request("snapshot")), sequence: 1 };
    let push!: (state: Snapshot) => void;
    let resolveRequest!: (state: Snapshot) => void;
    const pending = new Promise<Snapshot>((resolve) => {
      resolveRequest = resolve;
    });
    service.subscribe = async (callback) => {
      push = callback;
      return () => {};
    };
    service.request = () => pending;
    render(<App backend={service} />);
    await waitFor(() => expect(push).toBeDefined());
    const newer = {
      ...initial,
      sequence: 2,
      tasks: initial.tasks.map((status) =>
        status.task.id === "demo-epic"
          ? { ...status, isCompleted: true }
          : status,
      ),
    };
    act(() => push(newer));
    expect(
      screen.getByRole("checkbox", { name: "撤销完成 领取 Epic 本周免费游戏" }),
    ).toBeChecked();
    await act(async () => resolveRequest(initial));
    expect(
      screen.getByRole("checkbox", { name: "撤销完成 领取 Epic 本周免费游戏" }),
    ).toBeChecked();
  });

  it("旧ready=false响应不会覆盖已恢复的新快照或产生错误提示", async () => {
    const service = backend();
    const initial = { ...(await service.request("snapshot")), sequence: 1 };
    let push!: (state: Snapshot) => void;
    let resolveRequest!: (state: Snapshot) => void;
    const pending = new Promise<Snapshot>((resolve) => {
      resolveRequest = resolve;
    });
    service.subscribe = async (callback) => {
      push = callback;
      return () => {};
    };
    service.request = async (method) =>
      method === "snapshot" ? initial : pending;
    render(<App backend={service} />);
    await userEvent.click(
      await screen.findByRole("checkbox", {
        name: "完成 领取 Epic 本周免费游戏",
      }),
    );
    act(() => push({ ...initial, sequence: 2 }));
    await act(async () =>
      resolveRequest({ ...initial, ready: false, error: "旧的读取错误" }),
    );
    expect(screen.getByRole("button", { name: "新建任务" })).toBeVisible();
    expect(screen.queryByText("旧的读取错误")).not.toBeInTheDocument();
  });
  it("明确标识演示模式，完成后仍能在编辑器找到任务并撤销", async () => {
    render(<App backend={backend()} />);
    expect(await screen.findByText(/浏览器演示/)).toBeVisible();
    const checkbox = await screen.findByRole("checkbox", {
      name: "完成 领取 Epic 本周免费游戏",
    });
    await userEvent.click(checkbox);
    expect(
      await screen.findByRole("checkbox", {
        name: "撤销完成 领取 Epic 本周免费游戏",
      }),
    ).toBeChecked();
    await userEvent.click(
      screen.getByRole("checkbox", { name: "撤销完成 领取 Epic 本周免费游戏" }),
    );
    expect(
      await screen.findByRole("checkbox", {
        name: "完成 领取 Epic 本周免费游戏",
      }),
    ).not.toBeChecked();
  });

  it("后端写入失败时保持任务状态", async () => {
    const service = backend();
    const request = service.request;
    service.request = async (method, payload) => {
      if (method === "complete") throw new Error("无法写入数据");
      return request(method, payload);
    };
    render(<App backend={service} />);
    await userEvent.click(
      await screen.findByRole("checkbox", {
        name: "完成 领取 Epic 本周免费游戏",
      }),
    );
    expect(await screen.findByText("无法写入数据")).toBeVisible();
    expect(
      screen.getByRole("checkbox", { name: "完成 领取 Epic 本周免费游戏" }),
    ).not.toBeChecked();
  });

  it("切换历史显示完成与撤销事件", async () => {
    render(<App backend={backend()} />);
    await screen.findByText("领取 Epic 本周免费游戏");
    await userEvent.click(screen.getByRole("button", { name: /历史记录/ }));
    await waitFor(() => expect(screen.getByText("完成了任务")).toBeVisible());
  });

  it("演示设置更新明确说明没有真实保存", async () => {
    render(<App backend={backend()} />);
    await screen.findByText("领取 Epic 本周免费游戏");
    await userEvent.click(screen.getByRole("button", { name: "桌面提醒" }));
    await userEvent.click(screen.getByRole("switch", { name: "保持置顶" }));
    await userEvent.click(screen.getByRole("button", { name: "保存设置" }));
    expect(
      await screen.findByText("演示已更新，未保存到桌面应用"),
    ).toBeVisible();
  });

  it("归档要确认，确认后保留历史", async () => {
    render(<App backend={backend()} />);
    await userEvent.click(
      await screen.findByRole("button", { name: "更多操作 完成今日游戏日常" }),
    );
    await userEvent.click(screen.getByRole("menuitem", { name: "归档任务" }));
    expect(
      screen.getByRole("checkbox", { name: "撤销完成 完成今日游戏日常" }),
    ).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "确认归档" }));
    await waitFor(() =>
      expect(
        screen.queryByRole("checkbox", { name: "撤销完成 完成今日游戏日常" }),
      ).not.toBeInTheDocument(),
    );
    await userEvent.click(screen.getByRole("button", { name: "历史记录" }));
    expect(
      screen.getByRole("heading", { name: "完成今日游戏日常" }),
    ).toBeVisible();
  });

  it("后台出错后停止提供可写操作", async () => {
    const service = backend();
    let reportError: ((message: string) => void) | undefined;
    service.subscribe = async (_callback, onError) => {
      reportError = onError;
      return () => {};
    };
    render(<App backend={service} />);
    await screen.findByText("领取 Epic 本周免费游戏");
    act(() => reportError?.("后台进程已退出"));
    expect(await screen.findByText("后台进程已退出")).toBeVisible();
    expect(
      screen.queryByRole("button", { name: "新建任务" }),
    ).not.toBeInTheDocument();
  });

  it("读取损坏数据时必须明确确认后才恢复备份", async () => {
    const service = backend();
    const original = await service.request("snapshot");
    let restored = false;
    service.request = async (method) => {
      if (method === "restoreBackup") {
        restored = true;
        return original;
      }
      return {
        ...original,
        ready: false,
        error: "数据文件损坏",
        canRestoreBackup: true,
      };
    };
    render(<App backend={service} />);
    await userEvent.click(
      await screen.findByRole("button", { name: "从备份恢复" }),
    );
    expect(restored).toBe(false);
    await userEvent.click(screen.getByRole("button", { name: "确认恢复备份" }));
    expect(await screen.findByText("领取 Epic 本周免费游戏")).toBeVisible();
    expect(restored).toBe(true);
  });
});
