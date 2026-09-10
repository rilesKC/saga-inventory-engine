import express, { type Router } from "express";
import type { AddressInfo } from "node:net";

/** Mounts a router on a fresh Express app and listens on an ephemeral port -- real HTTP, no mocking library. */
export async function startTestServer(router: Router) {
  const app = express();
  app.disable("x-powered-by");
  app.use(express.json());
  app.use(router);

  const server = app.listen(0);
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const { port } = server.address() as AddressInfo;

  return { server, baseUrl: `http://localhost:${port}` };
}
