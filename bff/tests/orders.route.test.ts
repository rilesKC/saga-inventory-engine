import { describe, it, expect, afterEach } from "vitest";
import { createOrdersRouter } from "../src/routes/orders.js";
import { config } from "../src/config.js";
import { startTestServer } from "./testServer.js";

const orderPayload = { orderId: "ORDER-1", sku: "SKU-1", quantity: 4, amount: 199.99 };

let activeServer: { close: () => void } | undefined;

afterEach(() => {
  activeServer?.close();
  activeServer = undefined;
});

describe("POST /orders", () => {
  it.each([
    ["choreography", () => config.choreographyHostUrl],
    ["orchestration", () => config.orchestrationHostUrl],
  ] as const)("routes to %s host when stack=%s", async (stack, hostUrl) => {
    const calls: string[] = [];
    const fetchStub: typeof fetch = async (url) => {
      calls.push(url.toString());
      return new Response(null, { status: 202 });
    };
    const { server, baseUrl } = await startTestServer(createOrdersRouter(fetchStub));
    activeServer = server;

    const res = await fetch(`${baseUrl}/orders`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stack, ...orderPayload }),
    });

    expect(res.status).toBe(202);
    expect(calls).toEqual([`${hostUrl()}/orders`]);
  });

  it("returns 400 for an invalid stack value", async () => {
    const fetchStub: typeof fetch = async () => {
      throw new Error("should not be called");
    };
    const { server, baseUrl } = await startTestServer(createOrdersRouter(fetchStub));
    activeServer = server;

    const res = await fetch(`${baseUrl}/orders`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stack: "not-a-real-stack", ...orderPayload }),
    });

    expect(res.status).toBe(400);
  });
});

describe("GET /orders/:id", () => {
  it("proxies to the requested stack and passes through a 404", async () => {
    const calls: string[] = [];
    const fetchStub: typeof fetch = async (url) => {
      calls.push(url.toString());
      return new Response(null, { status: 404 });
    };
    const { server, baseUrl } = await startTestServer(createOrdersRouter(fetchStub));
    activeServer = server;

    const res = await fetch(`${baseUrl}/orders/ORDER-UNKNOWN?stack=orchestration`);

    expect(res.status).toBe(404);
    expect(calls).toEqual([`${config.orchestrationHostUrl}/orders/ORDER-UNKNOWN`]);
  });

  it("returns 400 for an id that doesn't match the allowed order-id format, without calling the host", async () => {
    const fetchStub: typeof fetch = async () => {
      throw new Error("should not be called");
    };
    const { server, baseUrl } = await startTestServer(createOrdersRouter(fetchStub));
    activeServer = server;

    const res = await fetch(`${baseUrl}/orders/${encodeURIComponent("has spaces")}?stack=choreography`);

    expect(res.status).toBe(400);
  });

  it("normalizes a successful response into the unified shape", async () => {
    const hostBody = { orderId: "ORDER-1", sku: "SKU-1", quantity: 4, amount: 199.99, status: "Reserved", history: [] };
    const fetchStub: typeof fetch = async () =>
      new Response(JSON.stringify(hostBody), { status: 200, headers: { "Content-Type": "application/json" } });
    const { server, baseUrl } = await startTestServer(createOrdersRouter(fetchStub));
    activeServer = server;

    const res = await fetch(`${baseUrl}/orders/ORDER-1?stack=choreography`);
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body).toEqual({ ...hostBody, stack: "choreography" });
  });
});
