# CosmicChimps.Aspire.Hosting.Umbral

Runs [Umbral](https://cosmic-chimps.github.io/umbral/), a self-hosted OpenID Connect provider, in an Aspire AppHost from its
published image, `ghcr.io/cosmic-chimps/umbral`. The package version is the image tag: version
`0.12.0` runs image `0.12.0`. `WithImageTag` overrides it.

## Run mode

```csharp
var umbral = builder.AddUmbral("umbral")      // host port 8080; AddUmbral("umbral", port: 8081) for another
    .WithDataVolume();                        // keeps /data: the operator credential, connectors, apps, keys

builder.AddProject<Projects.Web>("web")
    .WithReference(umbral)
    .WaitFor(umbral);
```

The port is fixed, not random: Umbral's public origin is the issuer in every token and each app's
redirect URI is registered against it.

With no public origin, run mode uses `http://localhost:<port>` and sets
`ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL__INSECURE_ALLOW_HTTP=true`, the pair that lets Umbral
accept plain http. The pair is set only when the origin is `http://` with `localhost`, `127.0.0.1` or
`[::1]` on the resource's port. An origin given with `WithPublicOrigin` applies in run mode too; any other
origin gets neither variable.

## Publish mode

```csharp
builder.AddUmbral("umbral")
    .WithDataVolume()
    .WithPublicOrigin("https://umbral-demo.example.com")   // or a ParameterResource
    .WithTrustedProxies("10.99.0.1");
```

Publishing never sets the laptop pair. It refuses to publish with no public origin, or with a string
origin that is not `https://`. A string origin is stored as Umbral reports its issuer (lowercase, no
default port, no trailing slash). An origin given as a parameter is passed as it is, so give it in that
form; Umbral itself refuses a plain-http, non-loopback address when it starts.

## WithReference

`WithReference(umbral)` gives the app `UMBRAL_ISSUER`, which is Umbral's public origin: the string you
gave, the parameter, or in run mode `http://localhost:<port>`. It is never the internal address
(`http://umbral:8080`), because an app validates the issuer against exactly what Umbral puts in its
tokens. It adds no `services__...` variables and no client ID or secret.

An app on your machine (`AddProject`) reaches the run-mode `http://localhost:<port>`. An app in a
container (`AddContainer`, `AddDockerfile`) cannot: `localhost` there is the container itself.
Run that app as a project while you develop, or deploy both with a public origin.

## Not done here yet

Claiming Umbral and registering an app (to get a client ID and secret) are still done in Umbral's
admin area, at `/admin`; the dashboard shows an "Admin" link to it.

Documentation: [Run Umbral from an Aspire AppHost](https://cosmic-chimps.github.io/umbral/run/aspire/),
and the rest of Umbral's at <https://cosmic-chimps.github.io/umbral/>.
