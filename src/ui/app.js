import React from "react";
import Link from "./components/Link";
import Meta from "./components/Meta";
import Page from "./components/Page";
import ReactRoot from "./components/ReactRoot";
import Script from "./components/Script";
import Title from "./components/Title";

const MOTIF = "q:motif";
const TYPE = "ui:type";

const TYPE_PAGE = "Page";
const TYPE_REACT_ROOT = "ReactRoot";

const COMPONENTS = {
  Link,
  Meta,
  [TYPE_PAGE]: Page, // Pedantic, but good to be explicit since we reference these keys below
  [TYPE_REACT_ROOT]: ReactRoot,
  Script,
  Title,
};

const HTML_TAG_NAME = /^[a-z][a-z0-9-]*$/;

const isMetadata = (key) => key.startsWith("q:") || key.startsWith("ui:");

function createReactComponents(data, root, suppressRender) {
  if (data === null || typeof data !== "object") {
    return suppressRender ? undefined : data;
  }

  if (Array.isArray(data)) {
    const items = data.map((item) =>
      createReactComponents(item, root, suppressRender),
    );
    return suppressRender ? items.find((x) => x) : items;
  }

  const type = data[TYPE];
  const component = Object.prototype.hasOwnProperty.call(COMPONENTS, type)
    ? COMPONENTS[type]
    : undefined;
  const isHtml = typeof type === "string" && HTML_TAG_NAME.test(type);
  if (component || isHtml) {
    if (suppressRender) {
      // On the client side, we only want to render descendents of react root
      if (data[TYPE] === TYPE_REACT_ROOT) {
        return createReactComponents(data.children, root, false);
      }

      if (isHtml) {
        return createReactComponents(data.children ?? null, root, true);
      }

      return Object.keys(data)
        .filter((key) => !isMetadata(key))
        .map((key) => createReactComponents(data[key], root, suppressRender))
        .find((x) => x);
    } else {
      const props = Object.keys(data).reduce((obj, key) => {
        if (isMetadata(key)) return obj;
        // Native attributes (including style objects) are props, not render trees.
        // Children and motif-backed attributes still resolve through Quilt.
        obj[key] =
          !isHtml || key === "children" || data[key]?.[MOTIF]
            ? createReactComponents(data[key], root, false)
            : data[key];
        return obj;
      }, {});
      return React.createElement(component || type, props);
    }
  }

  if (data[MOTIF]) {
    if (root[data[MOTIF]]) {
      return createReactComponents(root[data[MOTIF]], root, suppressRender);
    }

    // Allow default values
    if (data[data[MOTIF]]) {
      return createReactComponents(data[data[MOTIF]], root, suppressRender);
    }

    throw new Error(`Invalid ref in createReactComponents: ${data[MOTIF]}`);
  }

  throw new Error("Unexpected data passed to createReactComponents.");
}

export default function App(props) {
  return createReactComponents(props.patch, props.patch, props.isClient);
}
