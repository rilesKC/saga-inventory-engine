export const config = {
  choreographyHostUrl: process.env.CHOREOGRAPHY_HOST_URL ?? "http://localhost:5000",
  orchestrationHostUrl: process.env.ORCHESTRATION_HOST_URL ?? "http://localhost:5100",
};

export function hostUrlForStack(stack: unknown): string | null {
  if (stack === "choreography") return config.choreographyHostUrl;
  if (stack === "orchestration") return config.orchestrationHostUrl;
  return null;
}
