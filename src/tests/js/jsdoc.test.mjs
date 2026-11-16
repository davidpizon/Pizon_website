/**
 * Convention checks for the JavaScript: every top-level function, class, class method and exported
 * constant has a JSDoc block; nothing uses `var`; and every export is used somewhere (another module,
 * a test, or a .NET interop call).
 * @module jsdoc.test
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join, relative, extname } from "node:path";

const SRC = fileURLToPath(new URL("../../", import.meta.url));

/**
 * Lists files under a folder (skipping build output).
 * @param {string} dir The folder.
 * @param {string[]} extensions The extensions to keep.
 * @returns {string[]} Absolute file paths.
 */
function walk(dir, extensions) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (["bin", "obj", "node_modules", "TestResults"].includes(entry.name)) {
      continue;
    }
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      out.push(...walk(full, extensions));
    } else if (extensions.includes(extname(entry.name))) {
      out.push(full);
    }
  }
  return out;
}

/** Shipped and tool scripts (the code under the rules). */
const SHIPPED = [
  ...walk(join(SRC, "Pizon.Web.Client", "wwwroot", "js"), [".js", ".mjs"]),
  ...walk(join(SRC, "tools"), [".js", ".mjs"]),
];

/** Test scripts (documented too, and they count as users of exports). */
const TESTS = walk(join(SRC, "tests", "js"), [".mjs"]);

/** Keywords that look like method calls at class-body indentation but aren't declarations. */
const NOT_METHODS = new Set(["if", "for", "while", "switch", "return", "catch", "function"]);

/**
 * Finds declarations that need a JSDoc block.
 * @param {string} text The file text.
 * @returns {{line: number, name: string}[]} Declarations (1-based lines).
 */
function declarations(text) {
  const lines = text.split(/\r?\n/);
  const found = [];
  let inClass = false;
  lines.forEach((line, i) => {
    const top = /^(?:export\s+)?(?:default\s+)?(?:async\s+)?(?:function\*?|class)\s+([A-Za-z_$][\w$]*)/.exec(line) ??
      /^export\s+const\s+([A-Za-z_$][\w$]*)/.exec(line);
    if (top) {
      found.push({ line: i + 1, name: top[1] });
      inClass = /\bclass\b/.test(line) && !line.trimEnd().endsWith("}");
      return;
    }
    if (inClass && line.startsWith("}")) {
      inClass = false;
      return;
    }
    const method = inClass && /^ {2}(?:static\s+)?(?:async\s+)?(?:get\s+|set\s+)?(#?[A-Za-z_$][\w$]*)\s*\([^)]*\)\s*\{/.exec(line);
    if (method && !NOT_METHODS.has(method[1])) {
      found.push({ line: i + 1, name: method[1] });
    }
  });
  return found;
}

/**
 * Checks whether the lines just above a declaration end a JSDoc block.
 * @param {string[]} lines The file's lines.
 * @param {number} line The declaration's 1-based line.
 * @returns {boolean} True when documented.
 */
function hasJsDoc(lines, line) {
  const above = (lines[line - 2] ?? "").trim();
  if (!above.endsWith("*/")) {
    return false;
  }
  for (let i = line - 2; i >= 0; i--) {
    const t = lines[i].trim();
    if (t.startsWith("/**")) {
      return true;
    }
    if (t.startsWith("/*")) {
      return false;
    }
  }
  return false;
}

/**
 * Lists the names a module exports.
 * @param {string} text The module text.
 * @returns {string[]} Exported names.
 */
function exportedNames(text) {
  return [...text.matchAll(/^export\s+(?:async\s+)?(?:function\*?|class|const|let)\s+([A-Za-z_$][\w$]*)/gm)].map((m) => m[1]);
}

/**
 * Reads a file.
 * @param {string} file The path.
 * @returns {string} The text.
 */
function read(file) {
  return readFileSync(file, "utf8");
}

test("the scanner finds declarations and their docs", () => {
  const sample = ["/** Doc. */", "export function a() {}", "", "class B {", "  /** Doc. */", "  m() {", "    if (x) {", "  }", "  get g() {", "}", "/* not jsdoc */", "export const C = 1;"];
  const decls = declarations(sample.join("\n"));
  assert.deepEqual(decls.map((d) => d.name), ["a", "B", "m", "g", "C"]);
  assert.deepEqual(decls.map((d) => hasJsDoc(sample, d.line)), [true, false, true, false, false]);
  assert.equal(hasJsDoc(["x */", "function f() {}"], 2), false);
  assert.deepEqual(exportedNames("export const A = 1;\nexport async function b() {}\nconst c = 2;"), ["A", "b"]);
});

test("every function, class, method and exported constant has a JSDoc block", () => {
  const missing = [];
  for (const file of [...SHIPPED, ...TESTS]) {
    const text = read(file);
    const lines = text.split(/\r?\n/);
    for (const d of declarations(text)) {
      if (!hasJsDoc(lines, d.line)) {
        missing.push(`${relative(SRC, file)}:${d.line} ${d.name}`);
      }
    }
  }
  assert.deepEqual(missing, []);
});

test("nothing uses var", () => {
  const offenders = [...SHIPPED, ...TESTS]
    .filter((file) => /(^|[\s;{(])var\s+[A-Za-z_$]/m.test(read(file).replace(/\/\*[\s\S]*?\*\/|\/\/.*$/gm, "")))
    .map((file) => relative(SRC, file));
  assert.deepEqual(offenders, []);
});

test("every export of the terminal modules is used by another module, a test or .NET", () => {
  const modules = SHIPPED.filter((f) => f.includes(join("wwwroot", "js")));
  const csharp = walk(SRC, [".cs", ".razor"]).map(read).join("\n");
  const unused = [];
  for (const file of modules) {
    const others = [...SHIPPED, ...TESTS].filter((f) => f !== file).map(read).join("\n");
    for (const name of exportedNames(read(file))) {
      const word = new RegExp(`\\b${name.replace("$", "\\$")}\\b`);
      if (!word.test(others) && !csharp.includes(`"${name}"`)) {
        unused.push(`${relative(SRC, file)} ${name}`);
      }
    }
  }
  assert.deepEqual(unused, []);
});
