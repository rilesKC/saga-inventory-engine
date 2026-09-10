export const config = {
  choreographyHostUrl: process.env.CHOREOGRAPHY_HOST_URL ?? "http://localhost:5000",
  orchestrationHostUrl: process.env.ORCHESTRATION_HOST_URL ?? "http://localhost:5100",
};

export function hostUrlForStack(stack: unknown): string | null {
  if (stack === "choreography") return config.choreographyHostUrl;
  if (stack === "orchestration") return config.orchestrationHostUrl;
  return null;
}

// This is the actual trust boundary a request crosses (into a fetch() call to a .NET host) --
// client-side validation alone doesn't protect this, since anything can call the BFF directly.
export const ORDER_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

export function isValidOrderId(orderId: unknown): orderId is string {
  return typeof orderId === "string" && ORDER_ID_PATTERN.test(orderId);
}
