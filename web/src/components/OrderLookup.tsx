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
  readonly fetchFn?: typeof fetch;
};

// Order IDs are always assigned by PlaceOrderForm's own submission flow (see that component),
// so this is also a real constraint, not just an escape hatch -- an id outside this shape can't
// be one this app ever created.
const ORDER_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

export function OrderLookup({ fetchFn = fetch }: OrderLookupProps) {
  const [orderId, setOrderId] = useState("");
  const [stack, setStack] = useState<Stack>("choreography");
  const [result, setResult] = useState<OrderDetails | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [invalid, setInvalid] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setNotFound(false);
    setResult(null);
    setInvalid(false);

    if (!ORDER_ID_PATTERN.test(orderId) || (stack !== "choreography" && stack !== "orchestration")) {
      setInvalid(true);
      return;
    }

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
        <label htmlFor="lookup-order-id">Order ID</label>
        <input id="lookup-order-id" value={orderId} onChange={(e) => setOrderId(e.target.value)} />
        <label htmlFor="lookup-stack">Stack</label>
        <select id="lookup-stack" value={stack} onChange={(e) => setStack(e.target.value as Stack)}>
          <option value="choreography">Choreography</option>
          <option value="orchestration">Orchestration</option>
        </select>
        <button type="submit">Look Up</button>
      </form>
      {invalid && <p>Order ID must be 1-64 letters, digits, underscores, or hyphens.</p>}
      {notFound && <p>Order not found.</p>}
      {result && (
        <div>
          <p>Status: {result.status}</p>
          <ul>
            {result.history.map((event, i) => (
              <li key={`${i}-${event.eventType ?? "event"}`}>{event.eventType ?? JSON.stringify(event)}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
