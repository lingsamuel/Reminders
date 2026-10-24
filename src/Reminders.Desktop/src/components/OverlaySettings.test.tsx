import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import { OverlaySettings } from "./OverlaySettings";
import { createDemoRequest } from "../lib/demo";

it("任务快照更新但设置值不变时保留未保存的外观草稿", async () => {
  const snapshot = await createDemoRequest()("snapshot");
  const onSave = async () => {};
  const { rerender } = render(
    <OverlaySettings snapshot={snapshot} busy={false} onSave={onSave} />,
  );
  fireEvent.change(screen.getByLabelText("文字大小"), {
    target: { value: "24" },
  });
  fireEvent.change(screen.getByLabelText("不透明度"), {
    target: { value: "0.65" },
  });
  rerender(
    <OverlaySettings
      snapshot={{ ...snapshot, overlay: { ...snapshot.overlay }, tasks: [] }}
      busy={false}
      onSave={onSave}
    />,
  );
  expect(screen.getByLabelText("文字大小")).toHaveValue("24");
  expect(screen.getByLabelText("不透明度")).toHaveValue("0.65");
  expect(screen.getByRole("button", { name: "保存设置" })).toBeEnabled();
});
