export const BFF_URL = import.meta.env.VITE_BFF_URL ?? "http://localhost:4000";

export type Stack = "choreography" | "orchestration";

// Must match bff/src/config.ts's ORDER_ID_PATTERN. The BFF is the real enforcement point (this
// copy just lets the UI reject an invalid id before making a request), and PlaceOrderForm
// enforces this same pattern at creation time so a lookup here can never reject an id this app
// actually created.
export const ORDER_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

export function isValidOrderId(orderId: string): boolean {
  return ORDER_ID_PATTERN.test(orderId);
}
