# CosmicChimps.Aspire.Hosting.Umbral

Runs [Umbral](https://cosmic-chimps.github.io/umbral/), a self-hosted OpenID Connect provider, in an Aspire AppHost from its
published image, `ghcr.io/cosmic-chimps/umbral`. The package version is the image tag: version
`0.12.0` runs image `0.12.0`. `WithImageTag` overrides it.

## Run mode

```csharp
var umbral = builder.AddUmbral("umbral")      // host port 8080; AddUmbral("umbral", port: 8081) for another
    .WithDataVolume();                        // keeps /data: the operator credential, connectors, apps, keys

builder.AddDotnetProject("web", "../Web/Web.csproj")
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

An app on your machine (`AddDotnetProject`) reaches the run-mode `http://localhost:<port>`. An app in a
container (`AddContainer`, `AddDockerfile`) cannot: `localhost` there is the container itself.
Run that app as a project while you develop, or deploy both with a public origin.

## Declaring apps

Umbral can create the passkey connector and your apps itself when it starts, so there is nothing to click
in the admin area. The package writes the declaration (`UMBRAL__DECLARATION`) and each web app's secret
(`UMBRAL__APP_SECRETS__<key>`) into Umbral's environment:

```csharp
var umbral = builder.AddUmbral("umbral")
    .WithDataVolume()
    .WithPasskeys(PasskeySignUp.Open);            // the passkey connector; invitations only by default

var web = builder.AddDotnetProject("web", "../Web/Web.csproj");

var app = umbral.AddApp("web")                    // key = client ID, a web (confidential) app by default
    .WithDisplayName("Sample web app");
web.WithReference(app);                           // UMBRAL_ISSUER, UMBRAL_CLIENT_ID, UMBRAL_CLIENT_SECRET
```

- A web app gets a generated 48-character secret, `<umbral>-<key>-client-secret`, kept in the AppHost's user
  secrets in run mode and never written to the repository. `WithClientSecret(parameter)` uses your own.
  An `UmbralAppType.Spa` app is public: no secret, no `UMBRAL_CLIENT_SECRET`.
- The redirect URIs of a web app with none of its own are the `http` endpoint of the resource that references
  it, plus `/signin-oidc` and, as post-logout URI, `/signout-callback-oidc`. `WithCallbackPaths(signIn, signOut)`
  changes the paths, or turns this on for a spa app. `WithRedirectUri` and `WithPostLogoutRedirectUri` add
  explicit ones, and are required when publishing: an endpoint's address is not the public one, so publishing
  refuses derived URIs. An app referenced by two resources needs explicit URIs.
- `WithConnectors("passkey", ...)` limits the sign-in methods of an app; every shared connector otherwise.
- Signing in to a declared app needs no claim. To open the admin area, claim Umbral once at `/admin/setup`,
  the second link in the dashboard; the setup token is in Umbral's console log. With `PasskeySignUp.Open`,
  claim it before you publish it: until then anyone who reaches it can sign up.

Operating Umbral stays in its admin area, at `/admin`; the dashboard shows an "Admin" link to it.

Documentation: [Run Umbral from an Aspire AppHost](https://cosmic-chimps.github.io/umbral/run/aspire/),
and the rest of Umbral's at <https://cosmic-chimps.github.io/umbral/>.
