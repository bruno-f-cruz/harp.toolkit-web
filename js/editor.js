// CodeMirror editor holding the two metadata documents (device.yml and ios.yml), with
// schema-aware completion and diagnostic markers. CodeMirror 5 is loaded globally from the CDN.
import { complete, loadSchemas } from "./schema-complete.js";

const changeDelay = 150;

export async function createEditor(host, dotnet) {
  const CodeMirror = window.CodeMirror;
  const docs = {
    device: CodeMirror.Doc("", "yaml"),
    ios: CodeMirror.Doc("", "yaml"),
  };
  let current = "device";
  const markedLines = { device: [], ios: [] }; // line handles of the drawn error backgrounds
  const timers = {};

  const editor = CodeMirror(host, {
    value: docs.device,
    lineNumbers: true,
    indentUnit: 2,
    tabSize: 2,
    indentWithTabs: false,
    styleActiveLine: true,
    gutters: ["CodeMirror-linenumbers", "diagnostics"],
    extraKeys: {
      Tab: (cm) => (cm.somethingSelected() ? cm.indentSelection("add") : cm.replaceSelection("  ")),
      "Shift-Tab": (cm) => cm.indentSelection("subtract"),
      "Ctrl-Space": (cm) => showCompletions(cm),
      Esc: (cm) => cm.getInputField().blur(),
    },
    screenReaderLabel: "Metadata editor",
  });

  const { schemas, device, ios } = await loadSchemas(document.baseURI);
  const roots = { device, ios };

  function showCompletions(cm) {
    const root = roots[current];
    if (!root) return;
    cm.showHint({
      completeSingle: false,
      closeCharacters: /[\s()\[\]{};>,]/,
      hint: (cm) => {
        const cursor = cm.getCursor();
        const { items, prefix } = complete(cm.getValue(), cursor.line, cursor.ch, root, schemas);
        if (items.length === 0) return null;
        const indent = cm.getLine(cursor.line).match(/^\s*/)[0];
        return {
          from: CodeMirror.Pos(cursor.line, cursor.ch - prefix.length),
          to: cursor,
          list: items.map((item) => ({
            text: completionText(item, indent),
            render: (element) => renderItem(element, item),
            hint: (cm, data, completion) => {
              cm.replaceRange(completion.text, data.from, data.to, "complete");
              // After choosing a key whose value has known options, offer those right away.
              if (completion.text.endsWith(": ")) setTimeout(() => showCompletions(cm), 0);
            },
          })),
        };
      },
    });
  }

  function completionText(item, indent) {
    const cursor = editor.getCursor();
    const isValue = /:\s*[\w$.-]*$/.test(editor.getLine(cursor.line).slice(0, cursor.ch));
    if (isValue) return item.text;
    return item.block ? `${item.text}:\n${indent}  ` : `${item.text}: `;
  }

  function renderItem(element, item) {
    const label = document.createElement("span");
    label.className = "hint-label";
    label.textContent = item.label;
    element.appendChild(label);
    if (item.detail) {
      const detail = document.createElement("span");
      detail.className = "hint-detail";
      detail.textContent = item.detail;
      element.appendChild(detail);
    }
  }

  // Open completions as the user types a key, or a value that has known options.
  editor.on("inputRead", (cm, change) => {
    if (change.origin !== "+input" || cm.state.completionActive) return;
    const typed = change.text.join("");
    const cursor = cm.getCursor();
    const before = cm.getLine(cursor.line).slice(0, cursor.ch);
    if (/^[\w$]$/.test(typed) && /^\s*(-\s+)?[\w$.-]+$|:\s*[\w$.-]+$/.test(before)) showCompletions(cm);
    else if (typed === " " && /:\s$/.test(before)) showCompletions(cm);
  });

  for (const [name, doc] of Object.entries(docs)) {
    CodeMirror.on(doc, "change", (_, change) => {
      if (change.origin === "setValue") return;
      clearTimeout(timers[name]);
      timers[name] = setTimeout(() => dotnet.invokeMethodAsync("OnEditorChanged", name, doc.getValue()), changeDelay);
    });
  }

  // Diagnostics are kept per document and drawn on the visible one, since gutter markers
  // can only be placed through the editor showing the document.
  const diagnosticsByDoc = { device: [], ios: [] };
  function drawDiagnostics() {
    const doc = docs[current];
    for (const handle of markedLines[current]) doc.removeLineClass(handle, "background", "line-error");
    markedLines[current] = [];
    editor.clearGutter("diagnostics");
    for (const { line, message } of diagnosticsByDoc[current]) {
      if (line < 1 || line > doc.lineCount()) continue;
      const handle = doc.addLineClass(line - 1, "background", "line-error");
      markedLines[current].push(handle);
      const marker = document.createElement("span");
      marker.className = "diagnostic-marker";
      marker.title = message;
      marker.textContent = "●";
      editor.setGutterMarker(handle, "diagnostics", marker);
    }
  }

  return {
    setText(name, text) {
      clearTimeout(timers[name]);
      docs[name].setValue(text ?? "");
      docs[name].clearHistory();
    },
    show(name) {
      if (name === current) return;
      current = name;
      editor.swapDoc(docs[name]);
      drawDiagnostics();
    },
    setDiagnostics(name, diagnostics) {
      diagnosticsByDoc[name] = diagnostics;
      if (name === current) drawDiagnostics();
    },
    goTo(name, line, column) {
      this.show(name);
      const position = CodeMirror.Pos(line - 1, Math.max(0, column - 1));
      editor.setCursor(position);
      editor.scrollIntoView(position, editor.getScrollInfo().clientHeight / 3);
      editor.focus();
    },
    focus() {
      editor.focus();
    },
  };
}
