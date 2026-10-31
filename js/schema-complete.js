// Schema-aware completion for the YAML metadata files. It works on plain text and JSON schemas
// so it can be tested without an editor: find the key path at the cursor, walk the schema along
// it, and suggest the property names or values that fit.

const keyLine = /^(\s*)(?:-\s+)?([\w$.-]*)$/;
const valueLine = /^(\s*)(?:-\s+)?([\w$.-]+)\s*:\s*([\w$.-]*)$/;
const keyDeclaration = /^(\s*)(?:-\s+)?["']?([\w$.-]+)["']?\s*:/;

function isIgnorable(line) {
  const trimmed = line.trim();
  return trimmed === "" || trimmed.startsWith("#") || trimmed.startsWith("%") || trimmed.startsWith("---");
}

function indentOf(line) {
  return line.length - line.trimStart().length;
}

/// Finds the mapping keys enclosing the specified indentation, walking up from a line.
function parentPath(lines, lineIndex, indent) {
  const path = [];
  for (let i = lineIndex - 1; i >= 0 && indent > 0; i--) {
    if (isIgnorable(lines[i])) continue;
    const match = keyDeclaration.exec(lines[i]);
    const lineIndent = indentOf(lines[i]);
    if (match && lineIndent < indent) {
      path.unshift(match[2]);
      indent = lineIndent;
    }
  }
  return path;
}

/// Collects the keys already declared in the block containing the specified line.
function siblingKeys(lines, lineIndex, indent) {
  const keys = new Set();
  for (const step of [-1, 1]) {
    for (let i = lineIndex + step; i >= 0 && i < lines.length; i += step) {
      if (isIgnorable(lines[i])) continue;
      const lineIndent = indentOf(lines[i]);
      if (lineIndent < indent) break;
      const match = keyDeclaration.exec(lines[i]);
      if (match && lineIndent === indent) keys.add(match[2]);
    }
  }
  return keys;
}

/// Describes what is being typed at the cursor: a key in a mapping, or the value of a key.
export function cursorContext(text, line, ch) {
  const lines = text.split("\n");
  const before = (lines[line] ?? "").slice(0, ch);
  let match = valueLine.exec(before);
  if (match) {
    const indent = match[1].length;
    return {
      mode: "value",
      path: [...parentPath(lines, line, indent), match[2]],
      prefix: match[3],
      lines,
    };
  }

  match = keyLine.exec(before);
  if (match) {
    const indent = indentOf(before);
    return {
      mode: "key",
      path: parentPath(lines, line, indent),
      prefix: match[2],
      indent,
      siblings: siblingKeys(lines, line, indent),
      lines,
    };
  }
  return null;
}

/// Merges every schema fragment reachable through $ref, allOf, oneOf and anyOf.
export function resolve(fragments, schemas) {
  const result = { properties: new Map(), additional: [], enum: new Set(), types: new Set(), description: null };
  const seen = new Set();

  const visit = (node, base) => {
    if (!node || typeof node !== "object" || seen.has(node)) return;
    seen.add(node);
    if (node.$ref) {
      const [file, fragment] = node.$ref.split("#");
      const target = file ? new URL(file, base).href : base;
      let resolved = schemas.get(target);
      for (const part of (fragment ?? "").split("/").filter(Boolean)) resolved = resolved?.[part];
      visit(resolved, target);
    }
    result.description ??= node.description ?? null;
    for (const type of [node.type ?? []].flat()) result.types.add(type);
    for (const value of node.enum ?? []) result.enum.add(value);
    for (const [name, child] of Object.entries(node.properties ?? {})) {
      if (!result.properties.has(name)) result.properties.set(name, []);
      result.properties.get(name).push({ node: child, base });
    }
    if (node.additionalProperties && typeof node.additionalProperties === "object") {
      result.additional.push({ node: node.additionalProperties, base });
    }
    for (const key of ["allOf", "oneOf", "anyOf"]) {
      for (const child of node[key] ?? []) visit(child, base);
    }
  };

  for (const { node, base } of fragments) visit(node, base);
  return result;
}

function walk(root, path, schemas) {
  let fragments = [root];
  for (const key of path) {
    const resolved = resolve(fragments, schemas);
    fragments = resolved.properties.get(key) ?? resolved.additional;
    if (fragments.length === 0) return null;
  }
  return resolve(fragments, schemas);
}

/// Gets the keys declared directly under a top-level section, e.g. the names of all bit masks.
function declaredKeys(lines, section) {
  const keys = [];
  let inside = false;
  let childIndent = null;
  for (const line of lines) {
    if (isIgnorable(line)) continue;
    const indent = indentOf(line);
    const match = keyDeclaration.exec(line);
    if (indent === 0) {
      inside = match?.[2] === section;
      childIndent = null;
      continue;
    }
    if (!inside || !match) continue;
    childIndent ??= indent;
    if (indent === childIndent) keys.push(match[2]);
  }
  return keys;
}

/// Returns completion items for the cursor position, or an empty list.
/// Each item has the text to insert, a label, an optional detail and whether it opens a block.
export function complete(text, line, ch, root, schemas) {
  const context = cursorContext(text, line, ch);
  if (!context) return { items: [], prefix: "" };

  const prefix = context.prefix.toLowerCase();
  const matches = (label) => label.toLowerCase().startsWith(prefix);

  if (context.mode === "key") {
    const node = walk(root, context.path, schemas);
    if (!node) return { items: [], prefix: context.prefix };
    const items = [];
    for (const [name, fragments] of node.properties) {
      if (context.siblings.has(name) || !matches(name)) continue;
      const property = resolve(fragments, schemas);
      const isBlock = property.types.has("object") || property.properties.size > 0 || property.additional.length > 0;
      items.push({ label: name, text: name, detail: property.description, block: isBlock });
    }
    return { items, prefix: context.prefix };
  }

  const property = walk(root, context.path, schemas);
  if (!property) return { items: [], prefix: context.prefix };
  let values = [...property.enum].map(String);
  if (values.length === 0 && property.types.has("boolean")) values = ["true", "false"];

  // Mask types refer to masks declared in the same document.
  if (context.path.at(-1) === "maskType") {
    values = [...declaredKeys(context.lines, "bitMasks"), ...declaredKeys(context.lines, "groupMasks")];
  }

  const items = values
    .filter(matches)
    .map((value) => ({ label: value, text: value, detail: null, block: false }));
  return { items, prefix: context.prefix };
}

/// Loads the bundled metadata schemas, keyed by their canonical URL so $ref resolves.
export async function loadSchemas(baseUrl) {
  const names = ["device", "registers", "ios"];
  const schemas = new Map();
  await Promise.all(
    names.map(async (name) => {
      try {
        const response = await fetch(new URL(`schema/${name}.json`, baseUrl));
        if (!response.ok) return;
        const schema = await response.json();
        schemas.set(schema.$id ?? `https://harp-tech.org/draft-02/schema/${name}.json`, schema);
      } catch {
        // Completion is a convenience; the editor works without it.
      }
    }),
  );
  const root = (name) => {
    const id = `https://harp-tech.org/draft-02/schema/${name}.json`;
    return schemas.has(id) ? { node: schemas.get(id), base: id } : null;
  };
  return { schemas, device: root("device"), ios: root("ios") };
}
