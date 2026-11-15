import express from "express";
import renderDocument from "./template";
import { RenderDataError } from "./app";

const app = express();
app.disable("x-powered-by");
app.use(express.json({ limit: "1mb" }));

app.get("/health", (req, res) => res.sendStatus(204));

app.post("/", (req, res, next) => {
  if (!req.body || typeof req.body !== "object" || Array.isArray(req.body)) {
    return res.status(400).send("Invalid render data.");
  }
  try {
    return res.type("html").send(renderDocument(req.body));
  } catch (error) {
    return next(error);
  }
});

app.use((error, req, res, next) => {
  if (error instanceof RenderDataError || error instanceof SyntaxError || error.type === "entity.too.large") {
    return res.status(error.type === "entity.too.large" ? 413 : 400).send("Invalid render data.");
  }
  console.error("SSR render failed:", error);
  return res.status(500).send("Rendering failed.");
});

export default app;
