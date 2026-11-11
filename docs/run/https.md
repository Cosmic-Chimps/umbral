# Run it for real, behind HTTPS

Umbral serves plain HTTP on port 8080 inside the container and expects a reverse
proxy in front of it to terminate TLS. **The repository's
[`docker-compose.yml`](../../docker-compose.yml) does this with Caddy**, which also
fetches and renews the certificate for you.

Before you start:

1. Pick a **domain name** and point its DNS at the machine. A free dynamic-DNS
   name works.
2. Make ports **80 and 443** reach that machine. Caddy needs them to obtain the
   certificate. Until DNS and ports are right, the browser shows a TLS error,
   not an Umbral error.

Then, in the checkout:

```bash
UMBRAL_DOMAIN=auth.example.com docker compose up -d
docker compose logs umbral
```

(or put `UMBRAL_DOMAIN=auth.example.com` in a `.env` file next to the compose
file). `docker compose logs umbral` shows the setup URL and token, exactly as in
the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine):
look for the `=====` box at the end of the log, or
`docker compose logs umbral 2>&1 | grep -A6 "not configured yet"`. Continue from
[step 3](../../README.md#3-claim-the-instance) with the **https** address.

Umbral itself is never published on a port, so everything reaches it through
Caddy. **Do not publish port 8080 on a public host.**

The image has a built-in health check: `docker compose ps` shows `healthy` once
Umbral answers and can open its database. For another orchestrator, probe
`GET /healthz` on port 8080 (`200 ok`, or `503` when the database is unusable).

> Compose names the container after the folder you cloned into, so
> `docker exec umbral …` and `docker logs umbral` may not find it. Use
> `docker compose logs umbral`, `docker compose exec umbral …` and so on.

## The public origin must match exactly

`UMBRAL__PUBLIC_ORIGIN` is the one setting Umbral cannot start without, and it
must be **exactly the address people use**: scheme, host and port, with no path.
Umbral builds the issuer and every provider callback URL from it. It never
guesses from the incoming request, because behind a proxy the request says
something else (`http://umbral:8080`).

That value must byte-match what you registered in the provider's console. When
it does not, the provider answers **`redirect_uri_mismatch`** (or Google's
"Error 400: redirect_uri_mismatch"), and the login fails. The compose file builds
the origin from `UMBRAL_DOMAIN` so the origin and Caddy's address cannot drift.
If you change the origin, the callback URL changes, so register the new one with
every provider. The Connectors screen shows a warning banner whenever you reach
it at an address that differs from the configured origin.

Umbral cannot run under a sub-path (`https://example.com/auth`): the origin must
be a bare host.

## Using a different reverse proxy

nginx, Traefik and others work, if the proxy:

- terminates TLS and talks to `umbral:8080` over plain HTTP;
- sends **`X-Forwarded-Proto`**. Without `X-Forwarded-Proto: https`, Umbral
  sees plain http and answers `This server only accepts HTTPS requests.`;
- sends **`X-Forwarded-For`**, and replaces rather than trusts a value the
  client sent. Umbral rate-limits sign-in attempts per client address (an IPv6
  client is counted per /64, and an IPv4-mapped address like
  `::ffff:203.0.113.10` as its IPv4 form), and without it every visitor looks
  like the proxy and shares one limit.

Every URL Umbral publishes comes from `UMBRAL__PUBLIC_ORIGIN`, never from the
`Host` header, so the proxy's host handling does not matter. Caddy's
`reverse_proxy` does both of the above by default. nginx needs
`proxy_set_header X-Forwarded-Proto $scheme;` and
`proxy_set_header X-Forwarded-For $remote_addr;` written out.

**Who may send those headers.** By default Umbral believes `X-Forwarded-*` from
any sender, which is safe only while the proxy is the one thing that can reach
Umbral's port. If anything else can reach it (other containers on a shared
network, other apps on the same host), that thing can send its own
`X-Forwarded-For` on every request, pick its own client address and so dodge the
sign-in limits, and it can claim `https` or another host in the headers too. In
that case set `UMBRAL__TRUSTED_PROXIES` to the proxy's address, for example
`UMBRAL__TRUSTED_PROXIES=192.0.2.10`. Headers from every other connection
address are then ignored and that connection's real address is used. Umbral logs
one line at startup saying which proxies it trusts.

With the bundled docker-compose and Caddy setup you can leave it unset: only
Caddy is on that compose network, so only Caddy can reach Umbral.

Use the address your proxy's connections actually arrive from, and nothing a
neighbour can send from. A shared bridge's gateway is the wrong choice: other
containers that reach Umbral through a published host port often arrive
masqueraded as that gateway.
