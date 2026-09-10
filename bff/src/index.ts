import cors from "cors";
import express from "express";
import { createOrdersRouter } from "./routes/orders.js";

const app = express();
// Local-only, learning project -- the web app runs on a different origin (Vite dev server) than
// this BFF, so the browser needs CORS headers to allow the cross-origin fetch. No auth/production
// concerns here (see docs/specs/bff-web-client.md's Out of Scope), so a permissive default origin
// is fine rather than an allowlist.
app.use(cors({ origin: process.env.WEB_ORIGIN ?? "http://localhost:5173" }));
app.use(express.json());

app.get("/health", (_req, res) => {
  res.sendStatus(200);
});

app.use(createOrdersRouter());

const port = process.env.PORT ?? 4000;
app.listen(port, () => {
  console.log(`BFF listening on port ${port}`);
});

export { app };
