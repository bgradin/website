import React from "react";
import { renderToString } from "react-dom/server";
import { createRenderTree, RenderDataError } from "./app";
import Page from "./components/Page";
import Script from "./components/Script";
import ReactRoot from "./components/ReactRoot";

function serializePatch(patch) {
  return JSON.stringify(patch)
    .replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e")
    .replace(/&/g, "\\u0026")
    .replace(/\u2028/g, "\\u2028")
    .replace(/\u2029/g, "\\u2029");
}

// Own the complete HTTP document here. No HTML parsing or string insertion.
export default function renderDocument(patch) {
  const { element, root } = createRenderTree(patch);
  const document = element?.type === Page ? Page(element.props) : element;
  if (!React.isValidElement(document) || document.type !== "html") {
    throw new RenderDataError("The render data must have a Page or html root.");
  }
  const children = React.Children.toArray(document.props.children);
  const head = children.find((child) => child.type === "head");
  const body = children.find((child) => child.type === "body");
  if (children.length !== 2 || !head || !body) {
    throw new RenderDataError("A document must contain exactly one head and one body.");
  }

  const scripts = new Set();
  function collectScripts(node) {
    if (Array.isArray(node)) return node.forEach(collectScripts);
    if (!React.isValidElement(node)) return;
    if (node.type === "script" || node.type === Script) scripts.add(node.props.src);
    collectScripts(node.props.children);
  }
  collectScripts(document);
  // Legacy pages may declare the client script in head. Defer those scripts
  // until the body exists, and make page state available before they execute.
  function prepareHead(node) {
    if (Array.isArray(node)) return node.map(prepareHead);
    if (!React.isValidElement(node)) return node;
    if (node.type === ReactRoot) throw new RenderDataError("ReactRoot must be in the document body.");
    const managedScript = (node.type === "script" || node.type === Script) &&
      ["/assets/account.js", "/assets/main.js"].includes(node.props.src);
    return React.cloneElement(node, managedScript ? { defer: true } : {},
      prepareHead(node.props.children));
  }
  const state = <script key="page-state" dangerouslySetInnerHTML={{
    __html: `window.__patch = ${serializePatch(patch)};`,
  }} />;
  const assets = [];
  if (!scripts.has("/assets/account.js")) assets.push(<script key="account" src="/assets/account.js" />);
  if (root && !scripts.has("/assets/main.js")) assets.push(<script key="client" src="/assets/main.js" defer />);
  const html = React.cloneElement(document, {},
    React.cloneElement(head, {}, state, prepareHead(head.props.children)),
    React.cloneElement(body, {}, body.props.children, ...assets));
  return "<!DOCTYPE html>" + renderToString(html);
}
