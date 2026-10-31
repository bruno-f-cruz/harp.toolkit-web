// Browser helpers for the generator page. Everything here is a thin wrapper over DOM APIs
// that Blazor cannot reach directly.

export { createEditor } from "./editor.js";

const storagePrefix = "harp-generators:";

export function loadSetting(key) {
  try {
    return localStorage.getItem(storagePrefix + key);
  } catch {
    return null;
  }
}

export function saveSetting(key, value) {
  try {
    if (value === null || value === undefined) localStorage.removeItem(storagePrefix + key);
    else localStorage.setItem(storagePrefix + key, value);
  } catch {
    // Storage may be unavailable (private window, blocked site data); settings are a convenience.
  }
}

// Syntax highlighting is skipped for very large files to keep the page responsive.
const maxHighlightLength = 2_000_000;

export function highlight(element, code, language) {
  if (!element) return;
  if (window.hljs && code.length <= maxHighlightLength) {
    element.innerHTML = window.hljs.highlight(code, { language, ignoreIllegals: true }).value;
  } else {
    element.textContent = code;
  }
  element.parentElement.scrollTop = 0;
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

export function downloadText(fileName, text) {
  download(fileName, new Blob([text], { type: "text/plain;charset=utf-8" }));
}

export async function downloadBytes(fileName, streamReference) {
  const buffer = await streamReference.arrayBuffer();
  download(fileName, new Blob([buffer], { type: "application/zip" }));
}

function download(fileName, blob) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

export function openFilePicker(input) {
  input.value = "";
  input.click();
}

export function showDialog(dialog) {
  dialog.showModal();
}

export function closeDialog(dialog) {
  dialog.close();
}

// Dropping a file anywhere on the page loads it into the editor.
export function initDropTarget(dotnet) {
  let depth = 0;
  const root = document.documentElement;
  const hasFiles = (event) => Array.from(event.dataTransfer?.types ?? []).includes("Files");
  window.addEventListener("dragenter", (event) => {
    if (!hasFiles(event)) return;
    depth++;
    root.classList.add("dragging");
  });
  window.addEventListener("dragleave", () => {
    depth = Math.max(0, depth - 1);
    if (depth === 0) root.classList.remove("dragging");
  });
  window.addEventListener("dragover", (event) => {
    if (hasFiles(event)) event.preventDefault();
  });
  window.addEventListener("drop", async (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    depth = 0;
    root.classList.remove("dragging");
    const file = event.dataTransfer.files[0];
    if (file) await dotnet.invokeMethodAsync("LoadText", file.name, await file.text());
  });
}

export async function readFileInput(input) {
  const file = input.files?.[0];
  return file ? { name: file.name, text: await file.text() } : null;
}

// Keyboard shortcuts: Ctrl/Cmd+S downloads everything, Ctrl/Cmd+O opens a file.
export function initShortcuts(dotnet) {
  window.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey) return;
    const key = event.key.toLowerCase();
    if (key === "s" || key === "o") {
      event.preventDefault();
      dotnet.invokeMethodAsync("OnShortcut", key);
    }
  });
}

export function getQueryParameter(name) {
  return new URLSearchParams(window.location.search).get(name);
}

export function setQueryParameter(name, value) {
  const url = new URL(window.location.href);
  if (value) url.searchParams.set(name, value);
  else url.searchParams.delete(name);
  history.replaceState(null, "", url);
}

// Theme: follow the system unless the viewer picked one explicitly.
export function toggleTheme() {
  const root = document.documentElement;
  const current =
    root.dataset.theme ?? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  const next = current === "dark" ? "light" : "dark";
  root.dataset.theme = next;
  saveSetting("theme", next);
}
