import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/vitest";

// Without vitest's `globals: true`, Testing Library's auto-cleanup (which checks for a global
// afterEach) never registers -- unmount explicitly so each test starts from an empty document.
afterEach(cleanup);
