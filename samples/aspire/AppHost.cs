// The Aspire sample: Umbral from its published image, next to the ASP.NET Core sample
// (../aspnetcore), started together with `aspire run`. For a laptop. The ports are fixed
// on purpose: Umbral's public origin is the issuer in every token and the sample's redirect
// URI is registered against it, and both are exact addresses that Aspire's random ports
// would break.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

const string UmbralImage = "ghcr.io/cosmic-chimps/umbral";
// A pinned release, never "latest": the sample must not change under whoever runs it.
const string UmbralTag = "0.11.0";
const string VolumeName = "umbral-aspire-data";

var builder = DistributedApplication.CreateBuilder(args);

// The ports are fixed, not random: Umbral's public origin and the sample's redirect URI are
// registered once and must stay exact. They default to 8080 and 5000, and a key of the
// configuration (user secrets, for one) overrides each, because a laptop may already use one
// of them (on macOS, AirPlay Receiver listens on 5000).
var umbralPort = builder.Configuration.GetValue<int>("Umbral:Port", 8080);
var samplePort = builder.Configuration.GetValue<int>("Sample:Port", 5000);

// A plain string: an interpolated string handed to WithEnvironment becomes an Aspire reference expression.
var umbralOrigin = $"http://localhost:{umbralPort}";

var umbral = builder.AddContainer("umbral", UmbralImage, UmbralTag)
    // The host port is pinned and the origin is localhost: it is the issuer in every token,
    // and the sample's redirect URI is registered against it once.
    .WithHttpEndpoint(port: umbralPort, targetPort: 8080, name: "http")
    .WithEnvironment("UMBRAL__PUBLIC_ORIGIN", umbralOrigin)
    // The quick start's "plain http on localhost" pair. For a laptop only: never on a server.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("UMBRAL__INSECURE_ALLOW_HTTP", "true")
    // The operator credential, connectors, apps and signing keys. Wiping the volume means
    // claiming and registering again.
    .WithVolume(VolumeName, "/data")
    .WithHttpHealthCheck("/healthz");

// The parameters are declared only once they are set, because an unset parameter would make
// Aspire prompt for it. The sample is added only then too, because it refuses to start
// without them.
var clientIdValue = builder.Configuration["Parameters:umbral-client-id"];
var clientSecretValue = builder.Configuration["Parameters:umbral-client-secret"];

if (!string.IsNullOrWhiteSpace(clientIdValue) && !string.IsNullOrWhiteSpace(clientSecretValue))
{
    var clientId = builder.AddParameter("umbral-client-id");
    var clientSecret = builder.AddParameter("umbral-client-secret", secret: true);

    builder.AddProject<Projects.Umbral_Sample_AspNetCore>("sample")
        // The sample reads PORT and listens there; the port is pinned because the redirect URI
        // registered in Umbral names it.
        .WithHttpEndpoint(port: samplePort, env: "PORT")
        .WithEnvironment("UMBRAL_ISSUER", umbral.GetEndpoint("http"))
        .WithEnvironment("UMBRAL_CLIENT_ID", clientId)
        .WithEnvironment("UMBRAL_CLIENT_SECRET", clientSecret)
        .WaitFor(umbral);
}
else
{
    // What to do, once per volume. Never a secret value: only the steps. The labels in bold
    // are the admin area's own words (src/Umbral/Views/Texts.fs).
    var steps = $"""
        Umbral is starting at {umbralOrigin}, but the sample is not started yet: its client ID and secret are not set. Once per `{VolumeName}` volume:

        1. **Claim Umbral**: open {umbralOrigin}/admin/setup and paste the **Setup token** from Umbral's console logs (the `umbral` resource), then choose an **Operator credential**. Or, from a shell: `docker exec $(docker ps -q -f volume={VolumeName}) /app/Umbral reset-credential`, which claims Umbral and prints a password to sign in with (the filter is this sample's volume, which only its container mounts).
        2. On **Connectors**, choose **Passkey**, pick **Anyone can sign up** so that you can create your own account from the sign-in page, and press **Enable passkeys**. A passkey needs no account at any provider; any other connector works too, once it is registered with its provider.
        3. On **Apps**, press **Create app**: **Name** anything, **App type** **Web application**, **Redirect URI** `http://localhost:{samplePort}/signin-oidc`, **Post-logout redirect URI (optional)** `http://localhost:{samplePort}/signout-callback-oidc`. Copy the **Client ID** and the **Client secret** (shown once).
        4. From `samples/aspire`: `dotnet user-secrets set Parameters:umbral-client-id <client id>` and `dotnet user-secrets set Parameters:umbral-client-secret <client secret>`.
        5. Stop the AppHost and run `aspire run` again.
        """;

    // `aspire run` keeps the AppHost's console in its log file, so the steps also go to the
    // dashboard as a notification (a banner on the resources page) that stays until dismissed.
    Console.WriteLine(steps);
    builder.Eventing.Subscribe<AfterResourcesCreatedEvent>((e, ct) =>
    {
        var interaction = e.Services.GetRequiredService<IInteractionService>();
        if (interaction.IsAvailable)
        {
            // Not awaited: the task completes only when the notification is dismissed.
            _ = interaction.PromptNotificationAsync(
                "The sample is not started yet",
                steps,
                new NotificationInteractionOptions
                {
                    Intent = MessageIntent.Warning,
                    EnableMessageMarkdown = true,
                    LinkText = "Open Umbral's setup page",
                    LinkUrl = $"{umbralOrigin}/admin/setup",
                },
                ct);
        }
        return Task.CompletedTask;
    });
}

builder.Build().Run();
