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
          history: [{ eventType: "StockReserved" }],
          stack: "choreography",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    render(<OrderLookup fetchFn={fetchStub} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-1" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/status: reserved/i)).toBeInTheDocument());
    expect(screen.getByText(/stockreserved/i)).toBeInTheDocument();
  });

  it("looking up an unknown order renders a not-found message", async () => {
    const fetchStub: typeof fetch = async () => new Response(null, { status: 404 });
    render(<OrderLookup fetchFn={fetchStub} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-UNKNOWN" } });
    fireEvent.click(screen.getByRole("button", { name: /look up/i }));

    await waitFor(() => expect(screen.getByText(/not found/i)).toBeInTheDocument());
  });
});
