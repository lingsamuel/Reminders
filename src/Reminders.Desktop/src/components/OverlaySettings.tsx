import { useEffect, useState } from "react";
import {
  Check,
  Eye,
  Info,
  Loader2,
  Monitor,
  RotateCcw,
  Settings2,
} from "lucide-react";
import type { OverlaySettings as Settings, Snapshot } from "../lib/types";
import { errorText } from "../lib/format";

export function OverlaySettings({
  snapshot,
  busy,
  onSave,
  isDemo = false,
}: {
  snapshot: Snapshot;
  busy: boolean;
  isDemo?: boolean;
  onSave: (settings: Settings) => Promise<void>;
}) {
  const [settings, setSettings] = useState(snapshot.overlay);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  const { left, top, width, height, fontSize, opacity, topmost, visible } =
    snapshot.overlay;
  useEffect(() => {
    // 任务状态通知也会反序列化出新对象，只有设置字段实际变化才同步草稿。
    setSettings({
      left,
      top,
      width,
      height,
      fontSize,
      opacity,
      topmost,
      visible,
    });
  }, [left, top, width, height, fontSize, opacity, topmost, visible]);
  const update = (patch: Partial<Settings>) => {
    setSettings((old) => ({ ...old, ...patch }));
    setSaved(false);
  };
  const dirty = JSON.stringify(settings) !== JSON.stringify(snapshot.overlay);
  async function save() {
    setError("");
    try {
      await onSave(settings);
      setSaved(true);
    } catch (failure) {
      setError(errorText(failure));
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <div className="eyebrow">A GENTLE PRESENCE</div>
          <h1>刚刚好的存在感。</h1>
          <p className="subtitle">把桌面提醒调成你喜欢的样子。</p>
        </div>
        <button
          className="primary heading-action"
          disabled={busy || !dirty}
          onClick={save}
        >
          {busy ? <Loader2 size={14} className="spin" /> : <Check size={14} />}
          {saved && !dirty ? (isDemo ? "演示已更新" : "已保存") : "保存设置"}
        </button>
      </div>
      <div className="settings-grid">
        <section className="settings-panel">
          <h2 className="panel-title">
            <Settings2 size={16} />
            显示与外观
          </h2>
          <div className="settings-fields">
            <div className="switch-line">
              <div>
                <strong>显示桌面提醒</strong>
                <p>在桌面查看当前可执行的未完成任务。</p>
              </div>
              <button
                className={`switch ${settings.visible ? "on" : ""}`}
                role="switch"
                aria-label="显示桌面提醒"
                aria-checked={settings.visible}
                disabled={busy}
                onClick={() => update({ visible: !settings.visible })}
              />
            </div>
            <div className="switch-line">
              <div>
                <strong>保持置顶</strong>
                <p>全屏时仍会自动隐藏，不打扰沉浸时刻。</p>
              </div>
              <button
                className={`switch ${settings.topmost ? "on" : ""}`}
                role="switch"
                aria-label="保持置顶"
                aria-checked={settings.topmost}
                disabled={busy}
                onClick={() => update({ topmost: !settings.topmost })}
              />
            </div>
            <div className="settings-divider" />
            <div>
              <div className="range-heading">
                <label htmlFor="font-size">文字大小</label>
                <output>{settings.fontSize} px</output>
              </div>
              <input
                id="font-size"
                className="range-input"
                type="range"
                min="10"
                max="40"
                step="1"
                value={settings.fontSize}
                disabled={busy}
                onChange={(event) =>
                  update({ fontSize: Number(event.target.value) })
                }
              />
              <div className="range-extremes">
                <span>小一点</span>
                <span>大一点</span>
              </div>
            </div>
            <div>
              <div className="range-heading">
                <label htmlFor="opacity">不透明度</label>
                <output>{Math.round(settings.opacity * 100)}%</output>
              </div>
              <input
                id="opacity"
                className="range-input"
                type="range"
                min="0.15"
                max="1"
                step="0.01"
                value={settings.opacity}
                disabled={busy}
                onChange={(event) =>
                  update({ opacity: Number(event.target.value) })
                }
              />
              <div className="range-extremes">
                <span>更轻盈</span>
                <span>更清晰</span>
              </div>
            </div>
            <div className="settings-divider" />
            <div className="switch-line">
              <div>
                <strong>位置与尺寸</strong>
                <p>编辑器打开时，可直接拖动、缩放桌面提醒。</p>
              </div>
              <button
                className="text-button"
                disabled={busy}
                onClick={() =>
                  update({ left: 80, top: 100, width: 340, height: 260 })
                }
              >
                <RotateCcw size={12} />
                重置
              </button>
            </div>
            {error && (
              <div role="alert" className="form-error">
                {error}
              </div>
            )}
            {saved && !dirty && (
              <p role="status" style={{ color: "#4f7548", fontSize: 11 }}>
                {isDemo ? "演示已更新，未保存到桌面应用" : "设置已保存"}
                {snapshot.ready ? "" : "，请检查后台连接"}
              </p>
            )}
          </div>
        </section>
        <section className="settings-panel">
          <h2 className="panel-title">
            <Eye size={16} />
            外观预览
          </h2>
          <div className="preview-area">
            <div
              className="overlay-preview"
              style={{ fontSize: settings.fontSize, opacity: settings.opacity }}
            >
              <span>
                <i className="preview-check" />
                领取本周免费游戏
              </span>
              <span>
                <i className="preview-check" />
                完成今日的小目标
              </span>
            </div>
          </div>
          <p className="preview-caption">
            此处为样式预览，实际位置以桌面提醒为准。
          </p>
          <div className="settings-hint">
            <Monitor size={16} />
            <span>
              关闭或最小化编辑器后进入日常模式。
              <br />
              只有勾选框响应点击，文字和空白区域可穿透。
            </span>
          </div>
          <div className="settings-hint">
            <Info size={16} />
            <span>全部任务完成时自动隐藏。新周期到来时，再轻轻出现。</span>
          </div>
        </section>
      </div>
    </>
  );
}
