// The demo's AppHost. `aspire run` starts the three services on a laptop, as the Aspire sample
// (../../samples/aspire) does; `aspire deploy` publishes them to Caravana as one app,
// `umbral-demo`, through CosmicChimps.Aspire.Hosting.Caravana. Umbral comes from the hosting
// package's source (../../src/CosmicChimps.Aspire.Hosting.Umbral), whose default image tag is
// the last release in CHANGELOG.md; the sample is ../../samples/aspnetcore as it is. A release
// of Umbral deploys the demo (.github/workflows/demo.yml, and the README).

using CosmicChimps.Aspire.Hosting.Caravana;
using Microsoft.Extensions.Configuration;

// The public names. Each is a Caravana domain with a certificate of its own and needs a DNS A
// record of its own, DNS only (not proxied). Change them here and nowhere else, then deploy.
const string SiteDomain = "umbral-demo.lacalabaza.xyz";         // the landing and privacy pages
const string UmbralDomain = "auth.umbral-demo.lacalabaza.xyz";  // Umbral
const string SampleDomain = "app.umbral-demo.lacalabaza.xyz";   // the sample app

// Caravana's Traefik reaches a service from this address on a host its installer set up. It is
// the only sender whose X-Forwarded-* headers Umbral believes.
const string TraefikAddress = "10.99.0.1";

var builder = DistributedApplication.CreateBuilder(args);
var publishing = builder.ExecutionContext.IsPublishMode;

// Fixed ports for the laptop run: Umbral's origin is the issuer in every token and the sample's
// redirect URI is registered against its port, so neither can be random. A key of the
// configuration (user secrets) overrides each one; on macOS, AirPlay Receiver listens on 5000.
var umbralPort = builder.Configuration.GetValue<int>("Umbral:Port", 8080);
var samplePort = builder.Configuration.GetValue<int>("Sample:Port", 5000);
var sitePort = builder.Configuration.GetValue<int>("Site:Port", 5100);

// Where it is deployed, in publish mode only, so that `aspire run` asks for none of it. The
// parameters are prompted for when missing and read from Parameters__<name> in CI.
IResourceBuilder<CaravanaResource>? caravana = null;

if (publishing)
{
    var caravanaUrl = builder.AddParameter("caravana-url");                   // https://caravana.example.com
    var caravanaToken = builder.AddParameter("caravana-token", secret: true); // a deploy token for umbral-demo
    var registryEndpoint = builder.AddParameter("registry-endpoint");         // ghcr.io
    var registryRepository = builder.AddParameter("registry-repository");     // cosmic-chimps/umbral-demo
    builder.AddContainerRegistry("registry", registryEndpoint, registryRepository);

    caravana = builder.PublishToCaravana("caravana", s =>
    {
        s.Url = caravanaUrl.AsCaravanaValue();
        s.Token = caravanaToken.AsCaravanaValue();
        s.AppName = "umbral-demo";
        s.DisplayName = "Umbral demo";
    });
}

// Umbral: the last released image, its data in a volume of its own, and
// the passkey connector declared with sign-up open to anyone. The other connectors (GitHub,
// Google, Discord, Fediverse) are set up once in the admin area and live in that volume, with
// their secrets; the golden snapshot the nightly reset restores holds them.
var umbral = builder.AddUmbral("umbral", port: umbralPort)
    .WithDataVolume("umbral-demo-data")
    .WithPasskeys(PasskeySignUp.Open);

// `Umbral:ImageTag` runs another tag on a laptop (an image built from the Umbral repository,
// say). Never for a deploy: the demo runs the last released image.
if (!publishing && builder.Configuration["Umbral:ImageTag"] is { Length: > 0 } imageTag)
{
    umbral.WithImageTag(imageTag);
}

// The demo app in Umbral: client ID `demo`, a web app. Its secret is the Aspire secret parameter
// `umbral-demo-client-secret`, generated the first time and kept in this project's user secrets
// on a laptop; a deploy from CI reads it from Bella Baxter (see the README). The sample
// receives it with UMBRAL_ISSUER and UMBRAL_CLIENT_ID.
var demoApp = umbral.AddApp("demo")
    .WithDisplayName("Umbral demo");

// The ASP.NET Core sample of this repository, under the demo's own title.
var sample = builder.AddProject<Projects.Umbral_Sample_AspNetCore>("app")
    // The sample reads PORT and listens there; the redirect URIs of the laptop run follow it.
    .WithHttpEndpoint(port: samplePort, env: "PORT")
    .WithEnvironment("SAMPLE_TITLE", "Umbral demo")
    .WithReference(demoApp)
    .WaitFor(umbral);

// The landing and privacy pages: static files behind a small web server (site/Dockerfile),
// built and pushed by `aspire deploy` like the sample. They load nothing from another host.
var site = builder.AddDockerfile("site", "../site")
    .WithHttpEndpoint(port: sitePort, targetPort: 8080);

if (publishing)
{
    // Production settings. The package sets ASPNETCORE_ENVIRONMENT to nothing (so Production)
    // and never UMBRAL__INSECURE_ALLOW_HTTP when publishing; the origin must be https.
    umbral
        .WithPublicOrigin("https://" + UmbralDomain)
        .WithTrustedProxies(TraefikAddress)
        .WithCaravanaDomain(caravana!, UmbralDomain)
        .WithCaravanaHealthCheck(caravana!, "/healthz")
        .WithCaravanaMemory(caravana!, 256, 512)
        // The backup policy, so that "Back up now" and a restore exist for the service. Its
        // schedule is a cron line that never matches (the 31st of February): Caravana accepts it
        // and finds no slot, so no copy is ever taken on its own. The reset script takes the
        // weekly copy of the blank instance only after a restore that succeeded (reset/). `keep`
        // is high so the golden snapshot stays in retention until this Caravana can pin it
        // (README, "The golden snapshot").
        .WithCaravanaBackup(caravana!, CaravanaBackupEngine.Volume, schedule: "0 4 31 2 *", keep: 100);

    // A browser follows these, so they are the public address, never the service's.
    demoApp
        .WithRedirectUri($"https://{SampleDomain}/signin-oidc")
        .WithPostLogoutRedirectUri($"https://{SampleDomain}/signout-callback-oidc");

    sample
        // Behind Traefik, which terminates TLS: believe its X-Forwarded-* headers.
        .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
        .WithCaravanaDomain(caravana!, SampleDomain)
        .WithCaravanaHealthCheck(caravana!, "/public")
        .WithCaravanaMemory(caravana!, 128, 256);

    site
        .WithCaravanaDomain(caravana!, SiteDomain)
        .WithCaravanaHealthCheck(caravana!, "/health")
        .WithCaravanaMemory(caravana!, 16, 64);
}

builder.Build().Run();
