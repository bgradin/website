import app from "./server";

const server = app.listen(3000, "127.0.0.1", () => {
  console.log("SSR service listening on 127.0.0.1:3000");
});
server.on("error", (error) => {
  console.error("SSR service failed to start:", error);
  process.exitCode = 1;
});
process.on("SIGTERM", () => server.close(() => process.exit(0)));
process.on("SIGINT", () => server.close(() => process.exit(0)));
