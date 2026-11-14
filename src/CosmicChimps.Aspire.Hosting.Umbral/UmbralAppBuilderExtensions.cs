using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Declares the passkey connector and the apps of an Umbral resource, so Umbral creates them when it starts.
/// </summary>
public static partial class UmbralAppBuilderExtensions
{
    private const int SecretLength = 48;

    /// <summary>
    /// Declares the passkey connector: it exists, is enabled and is shared when Umbral starts.
    /// </summary>
    /// <param name="umbral">The Umbral resource builder.</param>
    /// <param name="signUp">Who can sign up with a passkey. Invitations only when not given.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<UmbralResource> WithPasskeys(
        this IResourceBuilder<UmbralResource> umbral,
        PasskeySignUp signUp = PasskeySignUp.InvitationsOnly)
    {
        ArgumentNullException.ThrowIfNull(umbral);

        umbral.Resource.Passkeys = signUp;
        EnsureDeclarationEnvironment(umbral);
        return umbral;
    }

    /// <summary>
    /// Declares an app: Umbral creates it when it starts, with the key as its client ID. A
    /// <see cref="UmbralAppType.Web"/> app gets a generated client secret, kept in the AppHost's user secrets in run mode.
    /// </summary>
    /// <param name="umbral">The Umbral resource builder.</param>
    /// <param name="key">The client ID: 1 to 32 lowercase letters, digits and hyphens.</param>
    /// <param name="type">Whether the app holds a secret (<see cref="UmbralAppType.Web"/>) or is public (<see cref="UmbralAppType.Spa"/>).</param>
    /// <returns>The app's resource builder, named <c>&lt;umbral&gt;-&lt;key&gt;</c>.</returns>
    /// <exception cref="ArgumentException">The key is not valid, or another app of this Umbral has it.</exception>
    public static IResourceBuilder<UmbralAppResource> AddApp(
        this IResourceBuilder<UmbralResource> umbral,
        string key,
        UmbralAppType type = UmbralAppType.Web)
    {
        ArgumentNullException.ThrowIfNull(umbral);
        ArgumentNullException.ThrowIfNull(key);

        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException(
                $"'{key}' is not a valid app key. Use 1 to 32 lowercase letters, digits and hyphens, for example 'web'.", nameof(key));
        }

        if (umbral.Resource.Apps.Any(a => a.Key == key))
        {
            throw new ArgumentException($"Umbral '{umbral.Resource.Name}' already has an app with the key '{key}'.", nameof(key));
        }

        var name = $"{umbral.Resource.Name}-{key}";
        var app = new UmbralAppResource(name, key, type, umbral.Resource);

        if (type == UmbralAppType.Web)
        {
            app.ClientSecret = AddGeneratedSecret(umbral.ApplicationBuilder, name).Resource;
        }

        umbral.Resource.AddApp(app);
        EnsureDeclarationEnvironment(umbral);

        // Nothing runs for an app: it is a declaration that Umbral reads, so it has no manifest entry.
        return umbral.ApplicationBuilder.AddResource(app).ExcludeFromManifest();
    }

    /// <summary>Sets the name Umbral shows for the app. The key when not set.</summary>
    /// <param name="app">The app's resource builder.</param>
    /// <param name="name">The display name, not empty, at most 100 characters.</param>
    /// <returns>The app's resource builder.</returns>
    public static IResourceBuilder<UmbralAppResource> WithDisplayName(this IResourceBuilder<UmbralAppResource> app, string name)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Trim().Length > 100)
        {
            throw new ArgumentException("A display name is at most 100 characters.", nameof(name));
        }

        app.Resource.DisplayName = name.Trim();
        return app;
    }

    /// <summary>Adds a redirect URI the app may sign in to. Umbral matches it exactly.</summary>
    /// <param name="app">The app's resource builder.</param>
    /// <param name="uri">An absolute URI, for example <c>https://app.example.com/signin-oidc</c>.</param>
    /// <returns>The app's resource builder.</returns>
    public static IResourceBuilder<UmbralAppResource> WithRedirectUri(this IResourceBuilder<UmbralAppResource> app, string uri)
    {
        ArgumentNullException.ThrowIfNull(app);
        CheckAbsolute(uri);

        app.Resource.AddRedirectUri(uri);
        return app;
    }

    /// <summary>Adds a post-logout redirect URI the app may send people back to after sign-out. Umbral matches it exactly.</summary>
    /// <param name="app">The app's resource builder.</param>
    /// <param name="uri">An absolute URI, for example <c>https://app.example.com/signout-callback-oidc</c>.</param>
    /// <returns>The app's resource builder.</returns>
    public static IResourceBuilder<UmbralAppResource> WithPostLogoutRedirectUri(this IResourceBuilder<UmbralAppResource> app, string uri)
    {
        ArgumentNullException.ThrowIfNull(app);
        CheckAbsolute(uri);

        app.Resource.AddPostLogoutRedirectUri(uri);
        return app;
    }

    /// <summary>
    /// Derives the redirect URIs from the <c>http</c> endpoint of the resource that references the app with
    /// <c>WithReference(app)</c>, followed by these paths. Not available when publishing: use
    /// <see cref="WithRedirectUri"/> with the public address.
    /// </summary>
    /// <param name="app">The app's resource builder.</param>
    /// <param name="signIn">The sign-in callback path. <c>/signin-oidc</c> when not given.</param>
    /// <param name="signOut">The sign-out callback path, or <see langword="null"/> for none. <c>/signout-callback-oidc</c> when not given.</param>
    /// <returns>The app's resource builder.</returns>
    /// <remarks>
    /// A <see cref="UmbralAppType.Web"/> app with no explicit redirect URI does this with the default paths
    /// without the call.
    /// </remarks>
    public static IResourceBuilder<UmbralAppResource> WithCallbackPaths(
        this IResourceBuilder<UmbralAppResource> app,
        string signIn = "/signin-oidc",
        string? signOut = "/signout-callback-oidc")
    {
        ArgumentNullException.ThrowIfNull(app);
        CheckPath(signIn, nameof(signIn));

        if (signOut is not null)
        {
            CheckPath(signOut, nameof(signOut));
        }

        app.Resource.CallbackPathsGiven = true;
        app.Resource.SignInPath = signIn;
        app.Resource.SignOutPath = signOut;
        return app;
    }

    /// <summary>
    /// Lets people sign in to the app only through these connectors, by slug. Every shared connector when not called.
    /// </summary>
    /// <param name="app">The app's resource builder.</param>
    /// <param name="slugs">One or more connector slugs, for example <c>passkey</c>.</param>
    /// <returns>The app's resource builder.</returns>
    /// <exception cref="ArgumentException">The list is empty or has an empty entry.</exception>
    public static IResourceBuilder<UmbralAppResource> WithConnectors(this IResourceBuilder<UmbralAppResource> app, params string[] slugs)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(slugs);

        if (slugs.Length == 0 || slugs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Give at least one connector slug, none of them empty.", nameof(slugs));
        }

        app.Resource.Connectors = slugs.Select(s => s.Trim()).ToList();
        return app;
    }

    /// <summary>Uses your own secret parameter for a <see cref="UmbralAppType.Web"/> app, in place of the generated one.</summary>
    /// <param name="app">The app's resource builder.</param>
    /// <param name="secret">A parameter holding a secret of at least 32 characters.</param>
    /// <returns>The app's resource builder.</returns>
    /// <exception cref="InvalidOperationException">The app is a <see cref="UmbralAppType.Spa"/> app, which holds no secret.</exception>
    public static IResourceBuilder<UmbralAppResource> WithClientSecret(
        this IResourceBuilder<UmbralAppResource> app,
        IResourceBuilder<ParameterResource> secret)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(secret);

        if (app.Resource.Type != UmbralAppType.Web)
        {
            throw new InvalidOperationException($"Umbral app '{app.Resource.Key}' is a spa app, which is public and holds no secret.");
        }

        // The generated parameter is not used any more: leave it out of the model and the manifest.
        if (app.Resource.ClientSecret is { } generated && !ReferenceEquals(generated, secret.Resource)
            && generated.Name == GeneratedSecretName(app.Resource.Name))
        {
            app.ApplicationBuilder.Resources.Remove(generated);
        }

        app.Resource.ClientSecret = secret.Resource;
        return app;
    }

    /// <summary>
    /// Gives a resource the client ID, and for a <see cref="UmbralAppType.Web"/> app the secret, of an app, with
    /// <c>UMBRAL_ISSUER</c> as in <see cref="UmbralResourceBuilderExtensions.WithReference{TDestination}(IResourceBuilder{TDestination}, IResourceBuilder{UmbralResource})"/>.
    /// </summary>
    /// <typeparam name="T">The type of the resource that signs in through the app.</typeparam>
    /// <param name="destination">The resource that signs in through the app.</param>
    /// <param name="app">The app.</param>
    /// <returns>The destination's resource builder.</returns>
    /// <remarks>
    /// Sets <c>UMBRAL_ISSUER</c>, <c>UMBRAL_CLIENT_ID</c> and, for a <see cref="UmbralAppType.Web"/> app,
    /// <c>UMBRAL_CLIENT_SECRET</c>. The resource is also the one whose <c>http</c> endpoint gives the app its callback URIs.
    /// </remarks>
    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> destination,
        IResourceBuilder<UmbralAppResource> app)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(app);

        var resource = app.Resource;
        resource.AddReferrer(destination.Resource);

        return UmbralResourceBuilderExtensions.WithIssuer(destination, resource.Parent)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["UMBRAL_CLIENT_ID"] = resource.Key;

                if (resource.ClientSecret is { } secret)
                {
                    context.EnvironmentVariables["UMBRAL_CLIENT_SECRET"] = secret;
                }
            });
    }

    /// <summary>Adds the environment of the declaration, once. The declaration is built when the resource starts, so endpoints can be resolved.</summary>
    private static void EnsureDeclarationEnvironment(IResourceBuilder<UmbralResource> umbral)
    {
        var resource = umbral.Resource;

        if (resource.DeclarationEnvironmentAdded)
        {
            return;
        }

        resource.DeclarationEnvironmentAdded = true;

        umbral.WithEnvironment(async context =>
        {
            context.EnvironmentVariables[UmbralDeclaration.DeclarationVariable] =
                await UmbralDeclaration.BuildAsync(resource, context.ExecutionContext, context.CancellationToken).ConfigureAwait(false);

            foreach (var app in resource.Apps.Where(a => a.ClientSecret is not null))
            {
                // The parameter itself, never its value: Aspire resolves it at start and writes a placeholder in the manifest.
                context.EnvironmentVariables[UmbralDeclaration.SecretVariable(app.Key)] = app.ClientSecret!;
            }
        });
    }

    private static string GeneratedSecretName(string appName) => $"{appName}-client-secret";

    private static IResourceBuilder<ParameterResource> AddGeneratedSecret(IDistributedApplicationBuilder builder, string appName) =>
        builder.AddParameter(
            GeneratedSecretName(appName),
            new GenerateParameterDefault { MinLength = SecretLength, Special = false },
            secret: true,
            persist: true);

    private static void CheckAbsolute(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"'{uri}' is not an absolute URI, for example https://app.example.com/signin-oidc.", nameof(uri));
        }
    }

    private static void CheckPath(string path, string parameter)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            throw new ArgumentException($"'{path}' is not a path. Start it with '/', for example /signin-oidc.", parameter);
        }
    }

    [GeneratedRegex(@"^[a-z0-9-]{1,32}\z")]
    private static partial Regex KeyPattern();
}
