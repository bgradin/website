import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import App from "../../src/ui/app";

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
  head: { "ui:type": "Title", text: "/" },
  body: { "ui:type": "ReactRoot", children: content },
};
const client = render(patch, true);
assert.equal(client, '<main id="welcome" class="page" aria-label="Welcome" data-page="home" style="color:red"><h1>Hello!</h1><p>Hello &lt;world&gt;</p><input disabled=""/></main>');
assert.equal(render(patch), '<html lang="en"><head><title>/</title></head><body><div class="content"><div id="app">' + client + '</div></div><script src="/assets/account.js"></script></body></html>');
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
  const html = render(page);
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
