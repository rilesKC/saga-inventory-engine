import { useState, type FormEvent } from "react";
import { BFF_URL, type Stack } from "../api";

type PlaceOrderFormProps = {
  /** Defaults to the global fetch; injectable so tests can stub the BFF call without a mocking library. */
  fetchFn?: typeof fetch;
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
      <label>
        Order ID
        <input value={orderId} onChange={(e) => setOrderId(e.target.value)} />
      </label>
      <label>
        SKU
        <input value={sku} onChange={(e) => setSku(e.target.value)} />
      </label>
      <label>
        Quantity
        <input type="number" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
      </label>
      <label>
        Amount
        <input type="number" value={amount} onChange={(e) => setAmount(e.target.value)} />
      </label>
      <label>
        Stack
        <select value={stack} onChange={(e) => setStack(e.target.value as Stack)}>
          <option value="choreography">Choreography</option>
          <option value="orchestration">Orchestration</option>
        </select>
      </label>
      <button type="submit">Place Order</button>
    </form>
  );
}
