# Run Umbral from an Aspire AppHost

If you build with [.NET Aspire](https://aspire.dev), the NuGet package
`CosmicChimps.Aspire.Hosting.Umbral` runs Umbral from your AppHost in one call, as
`AddKeycloak` does for Keycloak. Add it to the AppHost project:

```bash
dotnet add package CosmicChimps.Aspire.Hosting.Umbral
```

It starts the published image, `ghcr.io/cosmic-chimps/umbral`, and gives your apps
the issuer address. It does not claim Umbral or register your apps: you do that in
the admin area, once.

## Run it on your machine

```csharp
var umbral = builder.AddUmbral("umbral")
    .WithDataVolume();

builder.AddProject<Projects.Web>("web")
    .WithReference(umbral)
    .WaitFor(umbral);
```

`aspire run` then starts Umbral at `http://localhost:8080`. The port is fixed on
purpose: the public origin is the issuer in every token, and each app's redirect
addresses are registered against it once, so a random port would break both. To
use another port, pass it: `AddUmbral("umbral", port: 8081)`.

In run mode, with no public origin given, the package sets:

| Variable | Value |
| --- | --- |
| `UMBRAL__PUBLIC_ORIGIN` | `http://localhost:<port>` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
| `UMBRAL__INSECURE_ALLOW_HTTP` | `true` |

The last two are the laptop pair from the
[settings reference](settings.md): they let Umbral accept plain `http://`. They are
set only while the public origin is plain http on a loopback address (`localhost`,
`127.0.0.1` or `[::1]`). Any other origin given with `WithPublicOrigin` turns
them off.

Aspire waits for `/healthz` before it starts what you marked `WaitFor`, and the
dashboard links the resource to `/admin`.

`WithReference(umbral)` gives the app `UMBRAL_ISSUER`, set to Umbral's public
origin and never to its internal address (`http://umbral:8080`): an app checks the
issuer against exactly what Umbral writes in tokens. `UMBRAL_ISSUER` is the
variable every [sample](../../samples/README.md) reads.

## Claim Umbral and register your app

The first time, open `/admin` from the dashboard and
[claim the instance](../../README.md#3-claim-the-instance), then
[register your app](../../README.md#5-register-an-app) to get its client ID and
secret. Pass them to the app as Aspire parameters, the secret one marked `secret: true`:

```csharp
var clientId = builder.AddParameter("umbral-client-id");
var clientSecret = builder.AddParameter("umbral-client-secret", secret: true);

builder.AddProject<Projects.Web>("web")
    .WithReference(umbral)
    .WithEnvironment("UMBRAL_CLIENT_ID", clientId)
    .WithEnvironment("UMBRAL_CLIENT_SECRET", clientSecret)
    .WaitFor(umbral);
```

Set the two values in the AppHost's user secrets (`Parameters:umbral-client-id`
and `Parameters:umbral-client-secret`), so they stay out of the repository. The
package does not declare apps itself yet.

## Deploy it

`aspire publish` and `aspire deploy` need to know the address people will use, so
the public origin is required, and it must be `https://`:

```csharp
builder.AddUmbral("umbral")
    .WithDataVolume()
    .WithPublicOrigin("https://auth.example.com")
    .WithTrustedProxies("10.99.0.1");
```

`WithPublicOrigin` also takes an Aspire parameter, for an address you choose at
deploy time: `WithPublicOrigin(builder.AddParameter("umbral-origin"))`.

In publish mode the package never sets `ASPNETCORE_ENVIRONMENT=Development` or
`UMBRAL__INSECURE_ALLOW_HTTP`. Publishing without `WithPublicOrigin`, or with an
`http://` origin, fails with a message that names `WithPublicOrigin`.

Aspire does not put a reverse proxy in front of Umbral. Terminating TLS is yours,
as in [Run it for real, behind HTTPS](https.md); that page's
[Using a different reverse proxy](https.md#using-a-different-reverse-proxy) lists
what the proxy must send.

`WithTrustedProxies("10.99.0.1")` sets `UMBRAL__TRUSTED_PROXIES`, the addresses
whose `X-Forwarded-*` headers Umbral believes. Pass several as separate
arguments; they are joined with commas. See the [settings reference](settings.md).

## The image tag

The package's version is the image tag: package `X.Y.Z` runs
`ghcr.io/cosmic-chimps/umbral:X.Y.Z`, never `latest`. To run another tag, use
Aspire's own method:

```csharp
builder.AddUmbral("umbral")
    .WithImageTag("0.11.0");
```

## Your data

Everything Umbral keeps is in `/data`. `WithDataVolume()` mounts a Docker volume
there (pass a name to choose it); `WithDataBindMount("path")` mounts a folder
instead. It holds the operator credential, the connectors, the apps and the signing
keys: without it you claim Umbral and register your apps again. Back it up as
[Your data: back it up](backup.md) says.

## A working example

[`samples/aspire/`](../../samples/aspire/README.md) is an AppHost that runs
Umbral and the ASP.NET Core sample together.
