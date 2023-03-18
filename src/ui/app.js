import Link from "./components/Link";
import Meta from "./components/Meta";
import Page from "./components/Page";
import ReactRoot from "./components/ReactRoot";
import Script from "./components/Script";
import Title from "./components/Title";

const REF = "q:ref";
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

function createReactComponents(data, root, suppressRender) {
  if (typeof data !== "object") {
    return suppressRender ? undefined : data;
  }

  if (Array.isArray(data)) {
    const items = data.map((item) =>
      createReactComponents(item, root, suppressRender)
    );
    return suppressRender ? items.find((x) => x) : items;
  }

  if (data[TYPE] && COMPONENTS[data[TYPE]]) {
    if (suppressRender) {
      // On the client side, we only want to render descendents of react root
      if (data[TYPE] === TYPE_REACT_ROOT) {
        return createReactComponents(data.children, root, false);
      }

      return Object.keys(data)
        .map((key) => createReactComponents(data[key], root, suppressRender))
        .find((x) => x);
    } else {
      const props = Object.keys(data).reduce((obj, key) => {
        obj[key] = createReactComponents(data[key], root, suppressRender);
        return obj;
      }, {});
      return COMPONENTS[data[TYPE]](props);
    }
  }

  if (data[REF]) {
    if (root[data[REF]]) {
      return createReactComponents(root[data[REF]], root, suppressRender);
    }

    // Allow default values
    if (data[data[REF]]) {
      return createReactComponents(data[data[REF]], root, suppressRender);
    }

    console.warn(`Invalid ref in createReactComponents: ${data[REF]}`);
    return;
  }

  console.error("Unexpected data passed to createReactComponents:");
  console.error(data);
}

export default function App(props) {
  return createReactComponents(props.patch, props.patch, props.isClient);
}
