const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

test("browser-derived payload contains hostname only", () => {
  const source = fs.readFileSync(path.join(__dirname, "..", "background.js"), "utf8");
  assert.match(source, /const domain = parsed\.hostname\.toLowerCase\(\)/);
  assert.match(source, /body: JSON\.stringify\(\{domain\}\)/);
  assert.doesNotMatch(source, /tab\.title|parsed\.pathname|parsed\.search|chrome\.history/);
  assert.doesNotMatch(source, /JSON\.stringify\(\{[^}]*url/);
});
