import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import React from "react";
import { renderToStaticMarkup, renderToString } from "react-dom/server";
import App, { createRenderTree } from "../../src/ui/app";
import renderDocument from "../../src/ui/template";
import app from "../../src/ui/server";

const render = (patch, isClient = false) =>
  renderToStaticMarkup(React.createElement(App, { patch, isClient }));
const content = {
  "ui:type": "main",
  id: "welcome",
  className: "page",
  "aria-label": "Welcome",
  "data-page": "home",
  style: { color: "red" },
  "q:pinned": true,
  "ui:extra": "hidden",
  children: [
    { "ui:type": "h1", key: "heading", children: { "q:motif": "heading" } },
    { "ui:type": "p", key: "body", children: "Hello <world>" },
    { "ui:type": "input", key: "input", disabled: true },
    null,
  ],
};
const patch = {
  "ui:type": "Page",
  heading: "Hello!",
  head: { "ui:type": "Title", key: "title", text: "/" },
  body: { "ui:type": "ReactRoot", children: content },
};
const client = render(patch, true);
assert.equal(client, '<main id="welcome" class="page" aria-label="Welcome" data-page="home" style="color:red"><h1>Hello!</h1><p>Hello &lt;world&gt;</p><input disabled=""/></main>');
assert.equal(render(patch), '<html lang="en"><head><title>/</title></head><body><div class="content"><div id="app">' + client + '</div></div></body></html>');
assert.equal(render({ "ui:type": "a", href: { "q:motif": "url" }, url: "/about", children: "About" }).includes('href="/about"'), true);
assert.equal(render({ "ui:type": "br" }), "<br/>");
assert.equal(render(null), "");
assert.equal(render({ "ui:type": "my-widget", "data-value": "ready", children: "Custom" }), '<my-widget data-value="ready">Custom</my-widget>');
assert.equal(render({ "ui:type": "mian", children: "Accepted tag name" }), '<mian>Accepted tag name</mian>');
for (const type of ["Main", "toString", "1main", "main tag", "<main>", "", null, 42, {}]) {
  assert.throws(() => render({ "ui:type": type }), /Unexpected data/);
}
console.log("PASS native tags, attributes, motifs, metadata filtering, custom components, client rendering, nulls, custom elements, and invalid tag names");

for (const route of ["login", "register", "admin", "account"]) {
  const page = JSON.parse(readFileSync(`src/core/pages/${route}/index.json`, "utf8"));
  const html = renderDocument(page);
  assert.match(html, /<header/);
  assert.match(html, /href="\/account\/"/);
  assert.match(html, /data-session-visibility="signed-out"/);
  assert.match(html, /data-site-logout="true"/);
  assert.match(html, /data-session-visibility="signed-in"/);
  assert.equal((html.match(/src="\/assets\/account.js"/g) || []).length, 1);
  assert.equal((html.match(/<h1>/g) || []).length, 1);
  assert.match(html, /\/assets\/main.css/);
  assert.match(html, /\/assets\/account.js/);
  assert.match(html, /<title>.+Gradinware<\/title>/);
}
console.log("PASS bundled account pages render header, H1, title and supporting assets");

for (const value of [false, 0, "", null]) {
  assert.equal(createRenderTree({ "q:motif": "value", value }).element, value);
  assert.equal(createRenderTree({ "ui:type": "Page", head: null,
    body: { "q:motif": "fallback", fallback: value } }).element.props.body, value);
  assert.equal(createRenderTree({ "ui:type": "Page", value, head: null,
    body: { "q:motif": "value", value: "fallback" } }).element.props.body, value);
}
assert.throws(() => createRenderTree({ "q:motif": "loop", loop: { "q:motif": "loop" } }), /Cyclic/);
const cyclic = { "ui:type": "div" };
cyclic.children = cyclic;
assert.throws(() => createRenderTree(cyclic), /Cyclic/);
const literalCycle = { "ui:type": "div", style: {} };
literalCycle.style.self = literalCycle.style;
assert.throws(() => createRenderTree(literalCycle), /Cyclic/);
let deep = "leaf";
for (let i = 0; i < 130; i++) deep = { "ui:type": "div", children: deep };
assert.throws(() => createRenderTree(deep), /depth or node limit/);
assert.throws(() => createRenderTree(Array(10001).fill(null)), /depth or node limit/);
const expanding = { "ui:type": "Page", head: null, body: { "q:motif": "level0" } };
for (let i = 0; i < 14; i++) {
  expanding[`level${i}`] = { "ui:type": "span", children: [
    { "q:motif": `level${i + 1}` }, { "q:motif": `level${i + 1}` },
  ] };
}
expanding.level14 = "leaf";
assert.throws(() => createRenderTree(expanding), /depth or node limit/);
assert.throws(() => render({ "ui:type": "div" }, true), /requires one ReactRoot/);
const multipleRoots = { ...patch, body: [patch.body, patch.body] };
assert.throws(() => renderDocument(multipleRoots), /at most one ReactRoot/);
assert.throws(() => renderDocument({ "ui:type": "div", children: "<html></head>" }), /Page or html root/);
assert.throws(() => renderDocument({ "ui:type": "html", children: { "ui:type": "head" } }), /one head and one body/);
const nativeDocument = { "ui:type": "html", lang: "fr", children: [
  { "ui:type": "head", key: "head" },
  { "ui:type": "body", key: "body", children: { "ui:type": "p", children: "Bonjour" } },
] };
assert.match(renderDocument(nativeDocument), /^<!DOCTYPE html><html lang="fr">/);
const attack = "</script><script>alert(1)</script>&\u2028\u2029";
const html = renderDocument({ ...patch, heading: attack });
const clientMarkup = renderToString(React.createElement(App, { patch, isClient: true }));
assert.ok(renderDocument(patch).includes('<div id="app">' + clientMarkup + '</div>'));
assert.throws(() => renderDocument({ ...patch, head: patch.body, body: null }), /document body/);
assert.ok(html.startsWith("<!DOCTYPE html>"));
assert.ok(!html.includes(attack));
assert.match(html, /\\u003c\/script\\u003e/);
assert.equal((html.match(/src="\/assets\/main.js"/g) || []).length, 1);
assert.equal((html.match(/src="\/assets\/account.js"/g) || []).length, 1);
const scriptPatch = { ...patch, head: [patch.head, { "ui:type": "Script", key: "script", src: "/assets/main.js" }] };
assert.match(renderDocument(scriptPatch), /src="\/assets\/main.js" defer=""/);
assert.equal((renderDocument(scriptPatch).match(/src="\/assets\/main.js"/g) || []).length, 1);
console.log("PASS falsy motifs, cycle/depth limits, single root, document validation, doctype and script escaping");

async function checkService() {
  const server = app.listen(0, "127.0.0.1");
  await new Promise((resolve, reject) => {
    server.once("listening", resolve);
    server.once("error", reject);
  });
  const url = `http://127.0.0.1:${server.address().port}`;
  const post = (body) => fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body });
  try {
    assert.equal((await fetch(url + "/health")).status, 204);
    const response = await post(JSON.stringify(patch));
    assert.equal(response.status, 200);
    assert.match(response.headers.get("content-type"), /text\/html/);
    assert.equal(await response.text(), renderDocument(patch));
    for (const data of ["{", "null", "[]", JSON.stringify(multipleRoots), JSON.stringify({ "ui:type": "div" })]) {
      assert.equal((await post(data)).status, 400);
    }
    assert.equal((await post(JSON.stringify({ large: "x".repeat(1024 * 1024) }))).status, 413);
    console.log("PASS Express SSR endpoint, readiness, malformed input and payload limit");
  } finally {
    await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
}
checkService().catch(error => { console.error(error); process.exitCode = 1; });
