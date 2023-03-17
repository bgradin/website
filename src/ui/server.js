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
app.listen(3000);

app.use(express.json());

app.get("/", (req, res) => {
  const { patch, content } = render(req.body);
  const response = template(patch, content);
  res.send(response);
});
