import express from "express";
import { createOrdersRouter } from "./routes/orders.js";

const app = express();
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
