// The Aspire sample: Umbral, through the hosting package CosmicChimps.Aspire.Hosting.Umbral
// (../../src/CosmicChimps.Aspire.Hosting.Umbral), next to the ASP.NET Core sample
// (../aspnetcore), started together with `aspire run`. For a laptop. The AppHost declares the
// sample's app and the passkey connector, so Umbral creates them when it starts and the sample
// gets its issuer, client ID and secret: there is nothing to click in Umbral's admin area. The
// ports are fixed on purpose: Umbral's public origin is the issuer in every token and the
// sample's redirect URI is registered with it, and both are exact addresses that Aspire's
// random ports would break.

using Microsoft.Extensions.Configuration;

const string VolumeName = "umbral-aspire-data";

var builder = DistributedApplication.CreateBuilder(args);

// The ports are fixed, not random: Umbral's public origin and the sample's redirect URI must
// stay exact. They default to 8080 and 5000, and a key of the configuration (user secrets, for
// one) overrides each, because a laptop may already use one of them (on macOS, AirPlay Receiver
// listens on 5000).
var umbralPort = builder.Configuration.GetValue<int>("Umbral:Port", 8080);
var samplePort = builder.Configuration.GetValue<int>("Sample:Port", 5000);

// The image is the package's version (the last release here), on the fixed port, with the
// quick start's "plain http on localhost" pair, which run mode sets for a localhost origin.
var umbral = builder.AddUmbral("umbral", port: umbralPort)
    // The operator credential, connectors, apps and signing keys.
    .WithDataVolume(VolumeName)
    // The passkey connector, enabled, with sign-up open: you create your own account from the
    // sample's sign-in page. No account at any provider is needed.
    .WithPasskeys(PasskeySignUp.Open);

// `Umbral:ImageTag` runs another tag of the image, for one an image built from this repository
// (`docker build -t ghcr.io/cosmic-chimps/umbral:dev .` and `Umbral:ImageTag` = `dev`).
if (builder.Configuration["Umbral:ImageTag"] is { Length: > 0 } imageTag)
{
    umbral.WithImageTag(imageTag);
}

// The sample's app in Umbral: client ID `sample`, a web app with a generated secret kept in this
// AppHost's user secrets. Its redirect URIs are the sample's address plus /signin-oidc and
// /signout-callback-oidc, the ASP.NET Core defaults the sample uses.
var sampleApp = umbral.AddApp("sample")
    .WithDisplayName("ASP.NET Core sample");

builder.AddDotnetProject("sample", "../aspnetcore/Umbral.Sample.AspNetCore.csproj")
    // The sample reads PORT and listens there; the port is pinned because the redirect URI
    // declared above is taken from it.
    .WithHttpEndpoint(port: samplePort, env: "PORT")
    // UMBRAL_ISSUER (Umbral's public origin), UMBRAL_CLIENT_ID and UMBRAL_CLIENT_SECRET.
    .WithReference(sampleApp)
    .WaitFor(umbral);

builder.Build().Run();
