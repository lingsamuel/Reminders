import { invoke, isTauri } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { createDemoRequest } from "./demo";
import type { BackendApi, Snapshot } from "./types";

const native = isTauri();
export const api: BackendApi = {
  isDemo: !native,
  request: native
    ? (method, payload = {}) =>
        invoke<Snapshot>("backend_request", { method, payload })
    : createDemoRequest(),
  subscribe: native
    ? async (callback, onError) => {
        const stopState = await listen<Snapshot>("backend-state", (event) =>
          callback(event.payload),
        );
        try {
          const stopError = await listen<string>("backend-error", (event) =>
            onError?.(event.payload),
          );
          return () => {
            stopState();
            stopError();
          };
        } catch (error) {
          stopState();
          throw error;
        }
      }
    : async () => () => {},
  hide: async () => {
    if (native) await invoke("hide_editor");
  },
  exit: async () => {
    if (native) await invoke("exit_application");
  },
};
