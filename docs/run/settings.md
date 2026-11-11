# Settings reference

Set these as environment variables on the container.

| Variable | Default | What it does |
| --- | --- | --- |
| `UMBRAL__PUBLIC_ORIGIN` | *required* | The address people reach Umbral at, e.g. `https://auth.example.com`. Scheme, host, optional port; no path. `http` is accepted only for `localhost`. |
| `UMBRAL__TRUSTED_PROXIES` | unset (believe forwarded headers from any sender) | Comma-separated IP addresses and networks, IPv4 or IPv6, whose `X-Forwarded-For`, `-Proto` and `-Host` headers Umbral believes, e.g. `192.0.2.10, 10.0.0.0/8, fd00::/8`. Spaces around entries are ignored. Set it when anything other than your reverse proxy can reach Umbral's port, so it cannot choose its own client address and avoid the rate limits. A value that is not an address or network (including a network with bits set beyond its prefix, such as `10.0.0.5/8`) stops Umbral from starting and names the entry. See [Using a different reverse proxy](https.md#using-a-different-reverse-proxy). |
| `UMBRAL__DATA_DIRECTORY` | `/data` (set in the image) | Where Umbral keeps everything. Must be writable. |
| `ASPNETCORE_ENVIRONMENT` + `UMBRAL__INSECURE_ALLOW_HTTP` | unset | `Development` **and** `true` together allow plain `http://localhost`. Laptop trial only. Never set them on a server. |
| `UMBRAL__SECRET_STORE` | `local` | Where connector client secrets are kept: `local` (encrypted in `/data`), `aws`, `gcp`, `azure` or `bellabaxter`. Anything else stops the start. |
| `UMBRAL__SECRET_STORE_PREFIX` | `umbral` | Start of every secret's name in the manager. 1–32 lowercase letters, digits or hyphens, not starting or ending with a hyphen. |
| `UMBRAL__SECRET_CACHE_SECONDS` | `300` | How long a secret read from the manager is kept in memory. `0` turns the cache off. |
| `UMBRAL__SECRET_GRACE_SECONDS` | `3600` | How long Umbral keeps using the last secret it read when the manager cannot be reached. `0` refuses at once. |
| `UMBRAL__AZURE_KEY_VAULT_URI` | unset | The vault's `https://` address, no path. Required when the store is `azure`. |
| `UMBRAL__BELLABAXTER_PROVIDER` | unset | The BellaBaxter provider's slug; needed only when the environment has no secret provider or several. |

The cloud's own variables (`AWS_REGION`, `GOOGLE_CLOUD_PROJECT`, `AZURE_*`,
`BELLA_BAXTER_*`) are listed in each store's walkthrough:
[AWS](../secret-stores/aws.md) · [Google Cloud](../secret-stores/gcp.md) ·
[Azure](../secret-stores/azure.md) · [BellaBaxter](../secret-stores/bellabaxter.md).
