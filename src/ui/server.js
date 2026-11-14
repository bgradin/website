import express from "express";
import React from "react";
import { renderToString } from "react-dom/server";
import App from "./app";
import template from "./template";

function render(patch) {
  let content = renderToString(<App patch={patch} />);

  return { content, patch };
}

const app = express();
app.disable("x-powered-by");
app.use(express.json({ limit: "1mb" }));

app.post("/", (req, res, next) => {
  if (!req.body || typeof req.body !== "object" || Array.isArray(req.body)) {
    return res.status(400).send("Invalid render data.");
  }
  try {
    const { patch, content } = render(req.body);
    if (!content.includes("<html") || !content.includes("</head>")) {
      return res.status(400).send("The render data does not contain a page root.");
    }
    return res.type("html").send(template(patch, content));
  } catch (error) {
    return next(error);
  }
});

app.use((error, req, res, next) => {
  if (error instanceof SyntaxError || error.type === "entity.too.large") {
    return res.status(error.type === "entity.too.large" ? 413 : 400).send("Invalid render data.");
  }
  console.error("SSR render failed:", error);
  return res.status(500).send("Rendering failed.");
});

const server = app.listen(3000, "127.0.0.1", () => {
  console.log("SSR service listening on 127.0.0.1:3000");
});
server.on("error", (error) => {
  console.error("SSR service failed to start:", error);
  process.exitCode = 1;
});
process.on("SIGTERM", () => server.close(() => process.exit(0)));
process.on("SIGINT", () => server.close(() => process.exit(0)));
