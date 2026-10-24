import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { TaskEditor } from "./TaskEditor";

describe("任务编辑", () => {
  it("保存失败后重试复用同一草稿标识，避免重复创建", async () => {
    const save = vi
      .fn()
      .mockRejectedValueOnce(new Error("响应超时"))
      .mockResolvedValueOnce(undefined);
    render(<TaskEditor onSave={save} onClose={() => {}} />);
    await userEvent.type(screen.getByLabelText("任务名称"), "重试任务");
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    expect(await screen.findByText("响应超时")).toBeVisible();
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    expect(save).toHaveBeenCalledTimes(2);
    expect(save.mock.calls[1][0].id).toBe(save.mock.calls[0][0].id);
  });
  it("名称长度与 Core 的 120 字约束一致", async () => {
    render(<TaskEditor onSave={() => {}} onClose={() => {}} />);
    await userEvent.type(screen.getByLabelText("任务名称"), "a".repeat(121));
    expect(screen.getByLabelText("任务名称")).toHaveValue("a".repeat(120));
  });
  it("拒绝空名称，不向后端提交", async () => {
    const save = vi.fn();
    render(<TaskEditor onSave={save} onClose={() => {}} />);
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    expect(screen.getByText("请填写任务名称")).toBeVisible();
    expect(save).not.toHaveBeenCalled();
  });

  it("一次性任务必须有截止日期，保存保留所选日期和时间", async () => {
    const save = vi.fn().mockResolvedValue(undefined);
    render(<TaskEditor onSave={save} onClose={() => {}} />);
    await userEvent.type(screen.getByLabelText("任务名称"), "提交资料");
    await userEvent.click(screen.getByRole("radio", { name: /一次性/ }));
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    expect(screen.getByText("请选择截止日期")).toBeVisible();
    fireEvent.change(screen.getByLabelText("截止日期"), {
      target: { value: "2026-11-01" },
    });
    fireEvent.change(screen.getByLabelText("截止时间（可选）"), {
      target: { value: "18:30" },
    });
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    await waitFor(() =>
      expect(save).toHaveBeenCalledWith(
        expect.objectContaining({
          title: "提交资料",
          schedule: expect.objectContaining({
            kind: "Once",
            dueDate: "2026-11-01",
            dueTime: "18:30:00",
          }),
        }),
      ),
    );
  });

  it("每 N 天只接受正整数", async () => {
    const save = vi.fn();
    render(<TaskEditor onSave={save} onClose={() => {}} />);
    await userEvent.type(screen.getByLabelText("任务名称"), "检查体力");
    await userEvent.click(screen.getByRole("radio", { name: /每 N 天/ }));
    fireEvent.change(screen.getByLabelText("间隔天数"), {
      target: { value: "0" },
    });
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    expect(screen.getByText("间隔天数必须是正整数")).toBeVisible();
    expect(save).not.toHaveBeenCalled();
  });

  it("保存失败时保留编辑内容并显示错误", async () => {
    render(
      <TaskEditor
        onSave={async () => {
          throw new Error("磁盘写入失败");
        }}
        onClose={() => {}}
      />,
    );
    await userEvent.type(screen.getByLabelText("任务名称"), "晨间计划");
    await userEvent.click(screen.getByRole("button", { name: "创建任务" }));
    expect(await screen.findByText("磁盘写入失败")).toBeVisible();
    expect(screen.getByLabelText("任务名称")).toHaveValue("晨间计划");
  });
});
