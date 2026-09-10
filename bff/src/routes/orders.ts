import { Router, type Request, type Response } from "express";
import { hostUrlForStack, isValidOrderId } from "../config.js";

/**
 * fetchFn defaults to the global fetch but is injectable so tests can stub the downstream call --
 * no mocking library, same "manual test doubles" convention the .NET side uses.
 */
export function createOrdersRouter(fetchFn: typeof fetch = fetch): Router {
  const router = Router();

  router.post("/orders", async (req: Request, res: Response) => {
    const { stack, ...order } = req.body ?? {};
    const hostUrl = hostUrlForStack(stack);

    if (hostUrl === null) {
      res.status(400).json({ error: `invalid stack: ${stack}` });
      return;
    }

    const response = await fetchFn(`${hostUrl}/orders`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(order),
    });

    res.status(response.status).end();
  });

  router.get("/orders/:id", async (req: Request, res: Response) => {
    const { stack } = req.query;
    const hostUrl = hostUrlForStack(stack);

    if (hostUrl === null) {
      res.status(400).json({ error: `invalid stack: ${stack}` });
      return;
    }

    // Validated here, not just client-side -- this is the actual boundary the value crosses into
    // a fetch() call to a .NET host, and any caller can reach this route directly.
    if (!isValidOrderId(req.params.id)) {
      res.status(400).json({ error: `invalid order id: ${req.params.id}` });
      return;
    }

    const response = await fetchFn(`${hostUrl}/orders/${req.params.id}`);

    if (!response.ok) {
      res.status(response.status).end();
      return;
    }

    const body = await response.json();
    res.status(200).json({ ...body, stack });
  });

  return router;
}
