# Settings reference

Set these as environment variables on the container.

| Variable | Default | What it does |
| --- | --- | --- |
| `UMBRAL__PUBLIC_ORIGIN` | *required* | The address people reach Umbral at, e.g. `https://auth.example.com`. Scheme, host, optional port; no path. `http` is accepted only for `localhost`. |
| `UMBRAL__TRUSTED_PROXIES` | unset (believe forwarded headers from any sender) | Comma-separated IP addresses and networks, IPv4 or IPv6, whose `X-Forwarded-For`, `-Proto` and `-Host` headers Umbral believes, e.g. `192.0.2.10, 10.0.0.0/8, fd00::/8`. Spaces around entries are ignored. Set it when anything other than your reverse proxy can reach Umbral's port, so it cannot choose its own client address and avoid the rate limits. A value that is not an address or network (including a network with bits set beyond its prefix, such as `10.0.0.5/8`) stops Umbral from starting and names the entry. See [Using a different reverse proxy](https.md#using-a-different-reverse-proxy). |
| `UMBRAL__DATA_DIRECTORY` | `/data` (set in the image) | Where Umbral keeps everything. Must be writable. |
| `ASPNETCORE_ENVIRONMENT` + `UMBRAL__INSECURE_ALLOW_HTTP` | unset | `Development` **and** `true` together allow plain `http://localhost`. Laptop trial only. Never set them on a server. |
| `UMBRAL__BLUESKY_PLC_DIRECTORY` | `https://plc.directory` | Where the Bluesky connector reads `did:plc` documents. Change it only for a private PLC mirror. It must be an `https` address; `http` only with `ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL__INSECURE_ALLOW_HTTP=true`. Anything else stops the start and names the value. See the [Bluesky walkthrough](../connectors/bluesky.md). |
| `UMBRAL__SECRET_STORE` | `local` | Where connector client secrets are kept: `local` (encrypted in `/data`), `aws`, `gcp`, `azure` or `bellabaxter`. Anything else stops the start. |
| `UMBRAL__SECRET_STORE_PREFIX` | `umbral` | Start of every secret's name in the manager. 1–32 lowercase letters, digits or hyphens, not starting or ending with a hyphen. |
| `UMBRAL__SECRET_CACHE_SECONDS` | `300` | How long a secret read from the manager is kept in memory. `0` turns the cache off. |
| `UMBRAL__SECRET_GRACE_SECONDS` | `3600` | How long Umbral keeps using the last secret it read when the manager cannot be reached. `0` refuses at once. |
| `UMBRAL__AZURE_KEY_VAULT_URI` | unset | The vault's `https://` address, no path. Required when the store is `azure`. |
| `UMBRAL__BELLABAXTER_PROVIDER` | unset | The BellaBaxter provider's slug; needed only when the environment has no secret provider or several. |
| `UMBRAL__DECLARATION` | unset (no declaration) | A JSON document that declares the apps Umbral serves and, optionally, the passkey connector. Umbral applies it at every start, then shows what it declares as **Declared** and read-only in the admin area. It holds no secret. Unset or empty, nothing is applied, and anything still marked Declared is handed back to the admin area. A declaration that is wrong stops the start and the log says what to fix. See [Declare apps without Aspire](#declare-apps-without-aspire) below and [Declared apps](aspire.md#declared-apps). |
| `UMBRAL__APP_SECRETS__<key>` | none | The client secret of the declared web app `<key>`, at least 32 characters. One per web app, none for a spa app. Where a platform refuses `-` in a variable name, write `_` for it: `UMBRAL__APP_SECRETS__my_app` is the secret of `my-app`. Setting both forms for one key stops the start, and so does a variable for a key the declaration does not name: remove it with the app. Umbral stores only a hash and never logs the value, but it is an ordinary environment variable, visible to `docker inspect`. |

The cloud's own variables (`AWS_REGION`, `GOOGLE_CLOUD_PROJECT`, `AZURE_*`,
`BELLA_BAXTER_*`) are listed in each store's walkthrough:
[AWS](../secret-stores/aws.md) · [Google Cloud](../secret-stores/gcp.md) ·
[Azure](../secret-stores/azure.md) · [BellaBaxter](../secret-stores/bellabaxter.md).

## Declare apps without Aspire

The [Aspire package](aspire.md#declared-apps) writes these variables for you. In a
compose file, write them yourself. The JSON goes in a block scalar so the redirect
lists stay readable; the secrets come from the environment or an env file, not from
the compose file:

```yaml
services:
  umbral:
    image: ghcr.io/cosmic-chimps/umbral:0.12.0
    environment:
      UMBRAL__PUBLIC_ORIGIN: https://auth.example.com
      UMBRAL__DECLARATION: |
        {
          "version": 1,
          "passkeys": { "signUp": "invitations" },
          "apps": [
            {
              "key": "web",
              "name": "Our web app",
              "type": "web",
              "redirectUris": ["https://app.example.com/signin-oidc"],
              "postLogoutRedirectUris": ["https://app.example.com/signout-callback-oidc"],
              "connectors": "all-shared"
            }
          ]
        }
      UMBRAL__APP_SECRETS__web: ${WEB_CLIENT_SECRET}
```

The declaration needs Umbral 0.12.0 or later.

`version` is `1`. `passkeys` is optional: leave it out and the passkey connector is
not declared. `apps[].type` is `web` or `spa`; `connectors` is `"all-shared"` (the
default) or a list of connector slugs. The app's client ID is its `key`. Unknown
properties are refused, so a typo can't silently do nothing. Redirect URIs follow the admin
area's rules: exact, absolute, no wildcard, no fragment, at most 10 per list.

A declaration that cannot be applied stops Umbral, and `docker logs umbral` names
each problem with what to do. For what a declaration owns and what it leaves to the
admin area, how an app leaves it, and what to do before you run one in production,
see [Declared apps](aspire.md#declared-apps).
