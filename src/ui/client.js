import React from "react";
import { hydrateRoot } from "react-dom/client";
import App from "./app";

const patch = window.__patch;
delete window.__patch;

const root = document.querySelector("#app");
if (patch && root) {
  hydrateRoot(root, <App patch={patch} isClient />);
} else {
  console.error("Unable to hydrate: page state or app root is missing.");
}
