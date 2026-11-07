"use server";

import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { getToken } from "next-auth/jwt";
import { signOut } from "@/auth";

// Next.js checks a server action's Origin header against the Host, which is
// the sign-out form's anti-forgery protection: a form on another site cannot
// post to it.
export async function signOutEverywhere() {
  // Read before the cookie goes: the id_token is what Umbral's end-session endpoint wants.
  const token = await getToken({
    req: { headers: await headers() },
    secret: process.env.AUTH_SECRET!,
    secureCookie: process.env.AUTH_URL!.startsWith("https:"),
  });
  await signOut({ redirect: false }); // the app's own session: Auth.js's cookie

  const discovery = await fetch(`${process.env.UMBRAL_ISSUER!.replace(/\/+$/, "")}/.well-known/openid-configuration`, {
    cache: "no-store",
  });
  const endSession: string | undefined = (await discovery.json()).end_session_endpoint;
  if (!token?.idToken || !endSession) redirect("/");

  // id_token_hint names the app, so the post-logout redirect URI registered for
  // it is honoured. That URI is built from configuration, never from the
  // request, so it cannot become an open redirect.
  const url = new URL(endSession);
  url.searchParams.set("id_token_hint", token.idToken);
  url.searchParams.set("post_logout_redirect_uri", new URL("/signout-callback", process.env.AUTH_URL).href);
  redirect(url.href);
}
