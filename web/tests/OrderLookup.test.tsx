import { describe, it, expect } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { OrderLookup } from "../src/components/OrderLookup";

describe("OrderLookup", () => {
  it("looking up a known order renders its status and history", async () => {
    const fetchStub: typeof fetch = async () =>
      new Response(
        JSON.stringify({
          orderId: "ORDER-1",
          sku: "SKU-1",
          quantity: 4,
          amount: 199.99,
          status: "Reserved",
          history: [{ eventType: "StockReserved", payload: {} }],
          stack: "choreography",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    render(<OrderLookup fetchFn={fetchStub} notFoundRetryDelayMs={0} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-1" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/status: reserved/i)).toBeInTheDocument());
    expect(screen.getByText(/stockreserved/i)).toBeInTheDocument();
  });

  it("looking up an order not found on the first try, or the retry, renders a not-found message", async () => {
    const fetchStub: typeof fetch = async () => new Response(null, { status: 404 });
    render(<OrderLookup fetchFn={fetchStub} notFoundRetryDelayMs={0} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-UNKNOWN" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/not found/i)).toBeInTheDocument());
  });

  it("retries once on an initial 404 before giving up, finding the order if it appears on retry", async () => {
    // Covers the real eventual-consistency window: OrderIntakeHandler returns before saga
    // processing completes, so a lookup right after placing can legitimately 404 once.
    let callCount = 0;
    const fetchStub: typeof fetch = async () => {
      callCount += 1;
      if (callCount === 1) {
        return new Response(null, { status: 404 });
      }
      return new Response(
        JSON.stringify({
          orderId: "ORDER-1",
          sku: "SKU-1",
          quantity: 4,
          amount: 199.99,
          status: "Reserved",
          history: [{ eventType: "StockReserved", payload: {} }],
          stack: "choreography",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    };
    render(<OrderLookup fetchFn={fetchStub} notFoundRetryDelayMs={0} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-1" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/status: reserved/i)).toBeInTheDocument());
    expect(callCount).toBe(2);
  });

  it("shows an error message for a non-404 failure instead of crashing", async () => {
    const fetchStub: typeof fetch = async () => new Response(null, { status: 500 });
    render(<OrderLookup fetchFn={fetchStub} notFoundRetryDelayMs={0} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-1" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/lookup failed/i)).toBeInTheDocument());
  });

  it("rejects an order id outside the allowed format without calling the BFF", async () => {
    const calls: unknown[] = [];
    const fetchStub: typeof fetch = async () => {
      calls.push(1);
      throw new Error("should not be called");
    };
    render(<OrderLookup fetchFn={fetchStub} notFoundRetryDelayMs={0} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "has spaces" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/1-64 letters/i)).toBeInTheDocument());
    expect(calls).toHaveLength(0);
  });
});
