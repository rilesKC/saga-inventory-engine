import { useState, type FormEvent } from "react";
import { BFF_URL, type Stack } from "../api";

type PlaceOrderFormProps = {
  /** Defaults to the global fetch; injectable so tests can stub the BFF call without a mocking library. */
  readonly fetchFn?: typeof fetch;
};

export function PlaceOrderForm({ fetchFn = fetch }: PlaceOrderFormProps) {
  const [orderId, setOrderId] = useState("");
  const [sku, setSku] = useState("");
  const [quantity, setQuantity] = useState("");
  const [amount, setAmount] = useState("");
  const [stack, setStack] = useState<Stack>("choreography");

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    await fetchFn(`${BFF_URL}/orders`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        stack,
        orderId,
        sku,
        quantity: Number(quantity),
        amount: Number(amount),
      }),
    });
  }

  return (
    <form onSubmit={handleSubmit}>
      <h2>Place Order</h2>
      <label htmlFor="place-order-id">Order ID</label>
      <input id="place-order-id" value={orderId} onChange={(e) => setOrderId(e.target.value)} />
      <label htmlFor="place-sku">SKU</label>
      <input id="place-sku" value={sku} onChange={(e) => setSku(e.target.value)} />
      <label htmlFor="place-quantity">Quantity</label>
      <input id="place-quantity" type="number" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
      <label htmlFor="place-amount">Amount</label>
      <input id="place-amount" type="number" value={amount} onChange={(e) => setAmount(e.target.value)} />
      <label htmlFor="place-stack">Stack</label>
      <select id="place-stack" value={stack} onChange={(e) => setStack(e.target.value as Stack)}>
        <option value="choreography">Choreography</option>
        <option value="orchestration">Orchestration</option>
      </select>
      <button type="submit">Place Order</button>
    </form>
  );
}
