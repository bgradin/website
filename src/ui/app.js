import React from "react";
import Link from "./components/Link";
import Meta from "./components/Meta";
import Page from "./components/Page";
import ReactRoot from "./components/ReactRoot";
import Script from "./components/Script";
import Title from "./components/Title";

const MOTIF = "q:motif";
const TYPE = "ui:type";
const COMPONENTS = { Link, Meta, Page, ReactRoot, Script, Title };
const HTML_TAG_NAME = /^[a-z][a-z0-9-]*$/;
const has = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
const isMetadata = (key) => key.startsWith("q:") || key.startsWith("ui:");
const isTree = (value) => value && typeof value === "object" &&
  (has(value, TYPE) || has(value, MOTIF));

export class RenderDataError extends Error {}

// Motifs resolve against the complete patch, including falsy and null values.
function resolveMotif(data, root) {
  const name = data[MOTIF];
  if (typeof name !== "string") throw new RenderDataError("Invalid motif name.");
  if (has(root, name)) return root[name];
  if (has(data, name)) return data[name];
  throw new RenderDataError(`Invalid ref in createReactComponents: ${name}`);
}

export function createRenderTree(patch) {
  // Validate literal props too; cycles there must not escape into React.
  const ancestors = new Set();
  let inputNodes = 10000;
  function validate(value, depth = 0) {
    if (depth > 128 || --inputNodes < 0) throw new RenderDataError("Render data exceeds the depth or node limit.");
    if (value === null || typeof value !== "object") return;
    if (ancestors.has(value)) throw new RenderDataError("Cyclic render data.");
    ancestors.add(value);
    for (const child of Object.values(value)) validate(child, depth + 1);
    ancestors.delete(value);
  }
  validate(patch);
  const active = new Set();
  let remaining = 10000;

  function render(data, depth = 0) {
    if (depth > 128 || --remaining < 0) {
      throw new RenderDataError("Render data exceeds the depth or node limit.");
    }
    if (data === null || typeof data !== "object") return data;
    if (active.has(data)) throw new RenderDataError("Cyclic render data or motif reference.");
    active.add(data);
    try {
      if (Array.isArray(data)) return data.map((item) => render(item, depth + 1));
      const type = data[TYPE];
      const component = has(COMPONENTS, type) ? COMPONENTS[type] : undefined;
      const isHtml = typeof type === "string" && HTML_TAG_NAME.test(type);
      if (!component && !isHtml) {
        if (has(data, MOTIF)) return render(resolveMotif(data, patch), depth + 1);
        throw new RenderDataError("Unexpected data passed to createReactComponents.");
      }

      const props = {};
      for (const key of Object.keys(data)) {
        if (isMetadata(key)) continue;
        const value = data[key];
        // Attributes and structured props remain data. Only explicit trees,
        // children, and Page's named slots are interpreted as render trees.
        props[key] = key === "children" || (component === Page && (key === "head" || key === "body")) || isTree(value)
          ? render(value, depth + 1)
          : value;
      }
      return React.createElement(component || type, props);
    } finally {
      active.delete(data);
    }
  }

  const element = render(patch);
  const roots = [];
  function collect(node) {
    if (Array.isArray(node)) return node.forEach(collect);
    if (!React.isValidElement(node)) return;
    if (node.type === ReactRoot) roots.push(node);
    if (node.type === Page) {
      collect(node.props.head);
      collect(node.props.body);
    } else {
      collect(node.props.children);
    }
  }
  collect(element);
  if (roots.length > 1) throw new RenderDataError("A page may contain at most one ReactRoot.");
  return { element, root: roots[0] };
}

export default function App({ patch, isClient = false }) {
  const { element, root } = createRenderTree(patch);
  if (!isClient) return element;
  if (!root) throw new RenderDataError("Client rendering requires one ReactRoot.");
  return root.props.children;
}
