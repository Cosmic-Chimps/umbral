import { randomBytes } from "node:crypto";

for (const name of ["UMBRAL_ISSUER", "UMBRAL_CLIENT_ID", "UMBRAL_CLIENT_SECRET"]) {
  if (!process.env[name]) {
    console.error(`The environment variable ${name} is required.`);
    process.exit(1);
  }
}

// The app's public address, from configuration. Auth.js builds every URL it
// sends out (the redirect URI above all) from it and never from the Host or
// X-Forwarded-* headers of a request, which is why AUTH_TRUST_HOST is not
// needed. `||=` so that an empty AUTH_URL, as Compose passes it, counts as unset.
process.env.AUTH_URL ||= `http://localhost:${process.env.PORT || 3000}`;

// Random at every start, so there is no secret in the code; sessions end when
// the process restarts. Set AUTH_SECRET to keep them across restarts.
process.env.AUTH_SECRET ||= randomBytes(32).toString("base64url");
