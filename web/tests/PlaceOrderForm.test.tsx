import { describe, it, expect } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { PlaceOrderForm } from "../src/components/PlaceOrderForm";

describe("PlaceOrderForm", () => {
  it("submitting the form calls the BFF's POST /orders with the entered values", async () => {
    const calls: { url: string; body: unknown }[] = [];
    const fetchStub: typeof fetch = async (url, init) => {
      calls.push({ url: url.toString(), body: JSON.parse(init?.body as string) });
      return new Response(null, { status: 202 });
    };
    render(<PlaceOrderForm fetchFn={fetchStub} />);

    fireEvent.change(screen.getByLabelText(/order id/i), { target: { value: "ORDER-1" } });
    fireEvent.change(screen.getByLabelText(/sku/i), { target: { value: "SKU-1" } });
    fireEvent.change(screen.getByLabelText(/quantity/i), { target: { value: "4" } });
    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: "199.99" } });
    fireEvent.change(screen.getByLabelText(/stack/i), { target: { value: "orchestration" } });
    fireEvent.click(screen.getByRole("button", { name: /place order/i }));

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0].url).toMatch(/\/orders$/);
    expect(calls[0].body).toEqual({
      stack: "orchestration",
      orderId: "ORDER-1",
      sku: "SKU-1",
      quantity: 4,
      amount: 199.99,
    });
  });
});
