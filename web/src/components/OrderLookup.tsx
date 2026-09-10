import { useState, type FormEvent } from "react";
import { BFF_URL, isValidOrderId, type Stack } from "../api";

type HistoryEntry = { eventType: string; payload: unknown };

type OrderDetails = {
  orderId: string;
  sku: string;
  quantity: number;
  amount: number;
  status: string | null;
  history: HistoryEntry[];
  stack: string;
};

type OrderLookupProps = {
  /** Defaults to the global fetch; injectable so tests can stub the BFF call without a mocking library. */
  readonly fetchFn?: typeof fetch;
  /**
   * How long to wait before retrying once after an initial 404. A freshly-placed order can
   * legitimately 404 for a moment -- OrderIntakeHandler returns before saga processing completes
   * -- so one short retry covers the common case without building full polling. Overridable for
   * tests so they don't have to wait on the real default.
   */
  readonly notFoundRetryDelayMs?: number;
};

export function OrderLookup({ fetchFn = fetch, notFoundRetryDelayMs = 500 }: OrderLookupProps) {
  const [orderId, setOrderId] = useState("");
  const [stack, setStack] = useState<Stack>("choreography");
  const [result, setResult] = useState<OrderDetails | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [invalid, setInvalid] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setNotFound(false);
    setResult(null);
    setInvalid(false);
    setError(null);

    if (!isValidOrderId(orderId) || (stack !== "choreography" && stack !== "orchestration")) {
      setInvalid(true);
      return;
    }

    let response = await fetchFn(`${BFF_URL}/orders/${orderId}?stack=${stack}`);

    if (response.status === 404) {
      await new Promise((resolve) => setTimeout(resolve, notFoundRetryDelayMs));
      response = await fetchFn(`${BFF_URL}/orders/${orderId}?stack=${stack}`);
    }

    if (response.status === 404) {
      setNotFound(true);
      return;
    }

    if (!response.ok) {
      setError(`Lookup failed (status ${response.status}).`);
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
      {error && <p>{error}</p>}
      {result && (
        <div>
          <p>Status: {result.status}</p>
          <ul>
            {result.history.map((entry, i) => (
              <li key={`${i}-${entry.eventType}`}>{entry.eventType}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
