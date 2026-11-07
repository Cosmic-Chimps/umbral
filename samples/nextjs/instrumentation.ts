// Runs once when the server starts, before the first request. The work is in
// its own file, imported only on the Node.js runtime, so that the Edge bundle
// of this one never meets process.exit or node:crypto.
export async function register() {
  // `next build` runs this too, and a build has no settings.
  if (process.env.NEXT_RUNTIME === "nodejs" && process.env.NEXT_PHASE !== "phase-production-build") {
    await import("./instrumentation-node");
  }
}
