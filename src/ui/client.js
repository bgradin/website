import React from "react";
import { hydrate } from "react-dom";
import App from "./app";

const patch = window.__patch;
delete window.__patch;

hydrate(<App patch={patch} isClient />, document.querySelector("#app"));
