// Resizes the message dock of the Device page by dragging the divider above it, or with the
// arrow keys when the divider has focus. The chosen height is remembered per browser.

const storageKey = "harp-generators:dockHeight";
const minHeight = 120;
const keyStep = 32;

function clamp(height, container) {
  const max = Math.max(minHeight, container.clientHeight - 160);
  return Math.min(Math.max(height, minHeight), max);
}

function apply(container, height) {
  container.style.setProperty("--dock-height", `${Math.round(height)}px`);
}

function save(height) {
  try { localStorage.setItem(storageKey, String(Math.round(height))); } catch { }
}

export function initDock(handle, container) {
  try {
    const saved = Number(localStorage.getItem(storageKey));
    if (saved > 0) apply(container, clamp(saved, container));
  } catch { }

  // The handle sits at the top edge of the dock, so the dock spans from its parent's top to the container's bottom.
  const currentHeight = () => container.getBoundingClientRect().bottom - handle.parentElement.getBoundingClientRect().top;

  handle.addEventListener("pointerdown", event => {
    event.preventDefault();
    handle.setPointerCapture(event.pointerId);
    const bottom = container.getBoundingClientRect().bottom;
    const move = e => apply(container, clamp(bottom - e.clientY, container));
    const end = e => {
      handle.releasePointerCapture(e.pointerId);
      handle.removeEventListener("pointermove", move);
      handle.removeEventListener("pointerup", end);
      handle.removeEventListener("pointercancel", end);
      save(currentHeight());
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", end);
    handle.addEventListener("pointercancel", end);
  });

  handle.addEventListener("keydown", event => {
    const delta = event.key === "ArrowUp" ? keyStep : event.key === "ArrowDown" ? -keyStep : 0;
    if (delta === 0) return;
    event.preventDefault();
    const height = clamp(currentHeight() + delta, container);
    apply(container, height);
    save(height);
  });
}
