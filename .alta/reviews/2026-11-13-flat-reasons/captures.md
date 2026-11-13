# Captures for the design review of task fl4t (discovery reasons in one level of brackets)

Taken 2026-11-13 against the Release build of `alta/tranquil-grove-gqx6` (working tree with the fl4t change,
`dotnet build src/Umbral -c Release`, 0 warnings). Script: `capture.mjs` (Playwright, Chrome); its output is in
`capture-output.log`. Files are `<state>-<width>-<theme>.png`, widths 1280 and 360, themes dark and light.
**No capture overflows horizontally** (8 shots, every one `scrollWidth == innerWidth`).

## Instance

`dotnet Umbral.dll` from `bin/Release/net10.0`, `ASPNETCORE_ENVIRONMENT=Development`, `UMBRAL__INSECURE_ALLOW_HTTP=true`,
`UMBRAL__PUBLIC_ORIGIN=http://127.0.0.1:18080`, fresh data directory, and `HTTPS_PROXY=http://127.0.0.1:9` on the
Umbral process only, so every https check (Microsoft's) is refused at once. The OpenID Connect provider is added at
`http://127.0.0.1:9/realmN`, refused the same way. The instance is stopped.

## What changed

The reason a refused connection gives was `the connection failed (Connection refused (127.0.0.1:9))`; it is now
`the connection failed: Connection refused, 127.0.0.1:9`. The Apple and Microsoft notices keep their own brackets,
as ruled. **One wording change the task did not foresee:** the generic OpenID Connect notices did not bracket the
reason, they read `Umbral could not reach <issuer>: <reason>.` With the flat reason that is two colons in one
sentence, so they now read `Umbral could not reach <issuer> (<reason>).`, like Apple's and Microsoft's. Please rule
on that one.

## Captures

| State | Notice as shown | Overflow |
| --- | --- | --- |
| `oidc-added-unreachable` | "Umbral could not reach http://127.0.0.1:9/realm1 (the connection failed: Connection refused, 127.0.0.1:9). The provider was saved but is disabled — enable it once http://127.0.0.1:9/realm1/.well-known/openid-configuration opens in a browser." (each shot is a new provider, `realm1` to `realm4`, since one issuer has one connector) | no |
| `microsoft-saved-unreachable` | "Saved, but not enabled: Umbral could not reach Microsoft (the connection failed: Connection refused, 127.0.0.1:9). Press Enable to try again." | no |

The same reason reaches the Apple notices, the Microsoft "unchecked" notice and "Microsoft did not answer (…)"
unchanged; a test renders each of them and checks they have one level of brackets.
