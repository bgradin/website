import { createElement } from "react";
import Link from "./components/Link";
import Meta from "./components/Meta";
import Page from "./components/Page";
import ReactRoot from "./components/ReactRoot";
import Script from "./components/Script";
import Title from "./components/Title";

const REF = "q:ref";
const TYPE = "ui:type";

const TYPE_PAGE = "Page";

const COMPONENTS = {
  Link,
  Meta,
  [TYPE_PAGE]: Page, // Pedantic, but good to be explicit since we reference this key below
  ReactRoot,
  Script,
  Title,
};

function createReactComponents(data, root) {
  if (typeof data !== "object") {
    return data;
  }

  if (Array.isArray(data)) {
    return data.map((item) => createReactComponents(item, root));
  }

  if (data[TYPE] && COMPONENTS[data[TYPE]]) {
    const props = Object.keys(data).reduce((obj, key) => {
      obj[key] = createReactComponents(data[key], root);
      return obj;
    }, {});
    return COMPONENTS[data[TYPE]](props);
  }

  if (data[REF]) {
    if (root[data[REF]]) {
      return createReactComponents(root[data[REF]], root);
    }

    // Allow default values
    if (data[data[REF]]) {
      return createReactComponents(data[data[REF]], root);
    }

    console.warn(`Invalid ref in createReactComponents: ${data[REF]}`);
    return;
  }

  console.error("Unexpected data passed to createReactComponents:");
  console.error(data);
}

// If isClient is false, this should look for a react root and return that
function getBasePatch(patch, root, isClient) {
  const ref = patch[REF];
  const refPatch = ref && root[ref];
  return refPatch && (!isClient || refPatch[TYPE] !== TYPE_PAGE)
    ? getBasePatch(refPatch)
    : patch;
}

export default function App(props) {
  const base = getBasePatch(props.patch, props.patch, props.isClient);
  return createReactComponents(base, props.patch);
}
