import type { NextRequest } from "next/server";
import { signIn } from "@/auth";

export async function GET(request: NextRequest) {
  // Only a path of this app, from a list; never a value from the query passed through.
  const redirectTo = request.nextUrl.searchParams.get("to") === "/private" ? "/private" : "/";
  // Redirects to Umbral. It sets the PKCE, state and nonce cookies, which only
  // a route handler or a server action may do.
  await signIn("umbral", { redirectTo });
}
