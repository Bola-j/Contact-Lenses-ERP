export function createCrossTabSynchronizer({ onAuthChanged, onDataChanged }) {
  const channel = typeof globalThis.BroadcastChannel === "function" ? new BroadcastChannel("lensee-sync") : null;
  const onMessage = (event) => {
    if (event.data?.type === "auth-changed") onAuthChanged?.(event.data);
    if (event.data?.type === "data-changed") onDataChanged?.(event.data);
  };
  channel?.addEventListener("message", onMessage);
  const onStorage = (event) => {
    if (event.key === "lensee.auth") onAuthChanged?.({ source: "storage", value: event.newValue });
    if (event.key === "lensee.data-version") onDataChanged?.({ source: "storage", value: event.newValue });
  };
  globalThis.addEventListener?.("storage", onStorage);
  return {
    publish(type, detail = {}) {
      const message = { type, ...detail, at: Date.now() };
      channel?.postMessage(message);
      if (type === "data-changed") localStorage.setItem("lensee.data-version", String(message.at));
    },
    dispose() {
      channel?.removeEventListener("message", onMessage);
      channel?.close();
      globalThis.removeEventListener?.("storage", onStorage);
    }
  };
}
