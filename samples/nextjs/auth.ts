import NextAuth from "next-auth";

// The five claims the pages show, as they come in the id_token.
const CLAIMS = ["sub", "name", "idp", "email", "email_verified"] as const;

declare module "next-auth" {
  interface Session {
    claims?: Record<string, unknown>;
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    claims?: Record<string, unknown>;
    idToken?: string;
  }
}

// A function, not an object: it runs per request, after instrumentation.ts has
// checked the settings and made AUTH_URL and AUTH_SECRET.
export const { handlers, auth, signIn, signOut } = NextAuth(() => ({
  providers: [
    {
      id: "umbral",
      name: "Umbral",
      type: "oidc",
      // Discovery only: the endpoints are read from the issuer, never set by hand.
      issuer: process.env.UMBRAL_ISSUER,
      clientId: process.env.UMBRAL_CLIENT_ID,
      clientSecret: process.env.UMBRAL_CLIENT_SECRET, // a Web application is a confidential client
      // PKCE (S256), state and the id_token's nonce, all checked on the way back.
      checks: ["pkce", "state", "nonce"],
      authorization: { params: { scope: "openid profile email" } },
      // Invents nothing. No email: Auth.js lowercases it, and it replaces the
      // id with a random one. The pages read the claims below, not this user.
      profile(claims) {
        return { id: claims.sub, name: claims.name ?? null };
      },
    },
  ],
  // The session is the signed cookie, no database.
  // 14 days, as the other samples' sessions; Auth.js's default is 30.
  session: { strategy: "jwt", maxAge: 14 * 24 * 60 * 60 },
  callbacks: {
    jwt({ token, account, profile }) {
      if (account && profile) {
        // Sign-in: keep the id_token's validated claims, and the id_token itself,
        // which sign-out sends as id_token_hint.
        const claims: Record<string, unknown> = {};
        for (const name of CLAIMS) if (name in profile) claims[name] = profile[name];
        token.claims = claims;
        token.idToken = account.id_token;
      }
      return token;
    },
    session({ session, token }) {
      // Never the id_token: Auth.js serves the session as JSON at /api/auth/session.
      return { expires: session.expires, claims: token.claims };
    },
  },
}));
