using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Builds the value of <c>UMBRAL__DECLARATION</c> from the model, when Umbral starts or is published.
/// The format is <c>specs/019-declared-apps/contracts/declaration.md</c>, version 1.
/// </summary>
internal static class UmbralDeclaration
{
    internal const string DeclarationVariable = "UMBRAL__DECLARATION";
    internal const string SecretVariablePrefix = "UMBRAL__APP_SECRETS__";

    /// <summary>The variable that holds an app's secret: the key with <c>-</c> written as <c>_</c>.</summary>
    internal static string SecretVariable(string key) => SecretVariablePrefix + key.Replace('-', '_');

    internal static async ValueTask<string> BuildAsync(
        UmbralResource umbral, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        // Resolved first: it can throw, and it may wait for an endpoint.
        var uris = new List<(IReadOnlyList<string> Redirect, IReadOnlyList<string> PostLogout)>();

        foreach (var app in umbral.Apps)
        {
            uris.Add(await ResolveUrisAsync(app, executionContext, cancellationToken).ConfigureAwait(false));
        }

        using var stream = new MemoryStream();

        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);

            if (umbral.Passkeys is { } signUp)
            {
                writer.WriteStartObject("passkeys");
                writer.WriteString("signUp", signUp == PasskeySignUp.Open ? "open" : "invitations");
                writer.WriteEndObject();
            }

            writer.WriteStartArray("apps");

            for (var i = 0; i < umbral.Apps.Count; i++)
            {
                var app = umbral.Apps[i];

                writer.WriteStartObject();
                writer.WriteString("key", app.Key);

                if (app.DisplayName is { } displayName)
                {
                    writer.WriteString("name", displayName);
                }

                writer.WriteString("type", app.Type == UmbralAppType.Web ? "web" : "spa");
                WriteStrings(writer, "redirectUris", uris[i].Redirect);

                if (uris[i].PostLogout.Count > 0)
                {
                    WriteStrings(writer, "postLogoutRedirectUris", uris[i].PostLogout);
                }

                if (app.Connectors is { } connectors)
                {
                    WriteStrings(writer, "connectors", connectors);
                }
                else
                {
                    writer.WriteString("connectors", "all-shared");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    /// <summary>The explicit URIs, plus the referencing resource's http endpoint and the callback paths where they apply.</summary>
    private static async ValueTask<(IReadOnlyList<string> Redirect, IReadOnlyList<string> PostLogout)> ResolveUrisAsync(
        UmbralAppResource app, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        var redirect = new List<string>(app.RedirectUris);
        var postLogout = new List<string>(app.PostLogoutRedirectUris);

        if (app.EffectiveCallbackPaths is var (signIn, signOut))
        {
            if (executionContext.IsPublishMode)
            {
                throw new InvalidOperationException(
                    $"Umbral app '{app.Key}' takes its redirect URIs from the address of a resource, which a published app does not have. "
                    + $"Call WithRedirectUri(...) (and WithPostLogoutRedirectUri(...)) on the app with the public address, for example https://app.example.com{signIn}.");
            }

            if (app.Referrers.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Umbral app '{app.Key}' is referenced by {app.Referrers.Count} resources, so its callback paths do not say which one is meant. "
                    + "Call WithRedirectUri(...) on the app with each address, or reference the app from one resource only.");
            }

            if (app.Referrers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Umbral app '{app.Key}' has no redirect URI: nothing references it for its callback paths. "
                    + "Call WithReference(app) on the resource that signs in through it, or call WithRedirectUri(...) on the app.");
            }

            var origin = await EndpointOriginAsync(app, app.Referrers.Single(), cancellationToken).ConfigureAwait(false);

            AddOnce(redirect, origin + signIn);

            if (signOut is not null)
            {
                AddOnce(postLogout, origin + signOut);
            }
        }

        if (redirect.Count == 0)
        {
            throw new InvalidOperationException(
                $"Umbral app '{app.Key}' has no redirect URI. Call WithRedirectUri(...) on the app, or WithCallbackPaths() and reference it from the resource that signs in through it.");
        }

        return (redirect, postLogout);
    }

    private static async ValueTask<string> EndpointOriginAsync(UmbralAppResource app, IResource referrer, CancellationToken cancellationToken)
    {
        if (referrer is not IResourceWithEndpoints withEndpoints)
        {
            throw new InvalidOperationException(
                $"Umbral app '{app.Key}' is referenced by '{referrer.Name}', which has no endpoints to take a redirect URI from. Call WithRedirectUri(...) on the app.");
        }

        var endpoint = withEndpoints.GetEndpoint("http");

        if (!endpoint.Exists)
        {
            throw new InvalidOperationException(
                $"Umbral app '{app.Key}' is referenced by '{referrer.Name}', which has no endpoint named 'http' to take a redirect URI from. Call WithRedirectUri(...) on the app.");
        }

        var url = await endpoint.GetValueAsync(cancellationToken).ConfigureAwait(false);

        return (url ?? throw new InvalidOperationException($"The 'http' endpoint of '{referrer.Name}' has no address yet.")).TrimEnd('/');
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
