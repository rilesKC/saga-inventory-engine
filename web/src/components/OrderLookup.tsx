import { useState, type FormEvent } from "react";
import { BFF_URL, type Stack } from "../api";

type OrderDetails = {
  orderId: string;
  sku: string;
  quantity: number;
  amount: number;
  status: string | null;
  history: { eventType?: string }[];
  stack: string;
};

type OrderLookupProps = {
  /** Defaults to the global fetch; injectable so tests can stub the BFF call without a mocking library. */
  fetchFn?: typeof fetch;
};

export function OrderLookup({ fetchFn = fetch }: OrderLookupProps) {
  const [orderId, setOrderId] = useState("");
  const [stack, setStack] = useState<Stack>("choreography");
  const [result, setResult] = useState<OrderDetails | null>(null);
  const [notFound, setNotFound] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setNotFound(false);
    setResult(null);

    const response = await fetchFn(`${BFF_URL}/orders/${orderId}?stack=${stack}`);

    if (response.status === 404) {
      setNotFound(true);
      return;
    }

    const body = (await response.json()) as OrderDetails;
    setResult(body);
  }

  return (
    <div>
      <form onSubmit={handleSubmit}>
        <h2>Look Up Order</h2>
        <label>
          Order ID
          <input value={orderId} onChange={(e) => setOrderId(e.target.value)} />
        </label>
        <label>
          Stack
          <select value={stack} onChange={(e) => setStack(e.target.value as Stack)}>
            <option value="choreography">Choreography</option>
            <option value="orchestration">Orchestration</option>
          </select>
        </label>
        <button type="submit">Look Up</button>
      </form>
      {notFound && <p>Order not found.</p>}
      {result && (
        <div>
          <p>Status: {result.status}</p>
          <ul>
            {result.history.map((event, i) => (
              <li key={i}>{event.eventType ?? JSON.stringify(event)}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
