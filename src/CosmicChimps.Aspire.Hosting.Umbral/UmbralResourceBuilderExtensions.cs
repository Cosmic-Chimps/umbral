using System;
using System.Linq;
using System.Reflection;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Adds Umbral to an Aspire AppHost.
/// </summary>
public static class UmbralResourceBuilderExtensions
{
    private const string Registry = "ghcr.io";
    private const string Image = "cosmic-chimps/umbral";
    private const string DataPath = "/data";

    /// <summary>
    /// Adds Umbral, from <c>ghcr.io/cosmic-chimps/umbral</c> at the tag that is this package's version.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="port">The host port. 8080 when not given.</param>
    /// <returns>The resource builder.</returns>
    /// <remarks>
    /// In run mode with no public origin, Umbral is reached at <c>http://localhost:port</c> and started
    /// with the plain-http laptop pair. In publish mode a public https origin is required, see
    /// <see cref="WithPublicOrigin(IResourceBuilder{UmbralResource}, string)"/>.
    /// </remarks>
    public static IResourceBuilder<UmbralResource> AddUmbral(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var resource = new UmbralResource(name) { Port = port ?? UmbralResource.ContainerPort };

        return builder.AddResource(resource)
            .WithImage(Image, DefaultImageTag())
            .WithImageRegistry(Registry)
            // The host port is fixed, not random: the public origin is the issuer in every token
            // and each app's redirect URI is registered against it once, so both must stay exact.
            .WithHttpEndpoint(port: resource.Port, targetPort: UmbralResource.ContainerPort, name: UmbralResource.HttpEndpointName)
            .WithHttpHealthCheck("/healthz")
            .WithUrlForEndpoint(UmbralResource.HttpEndpointName, _ => new ResourceUrlAnnotation { Url = "/admin", DisplayText = "Admin" })
            .WithEnvironment(async context =>
            {
                var origin = await UmbralOrigin.ResolveAsync(resource, context.ExecutionContext, context.CancellationToken).ConfigureAwait(false);

                context.EnvironmentVariables["UMBRAL__PUBLIC_ORIGIN"] = origin.Value;

                if (origin.LaptopPair)
                {
                    // Plain http on localhost, for a laptop only: Umbral wants both together.
                    context.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
                    context.EnvironmentVariables["UMBRAL__INSECURE_ALLOW_HTTP"] = "true";
                }

                if (resource.TrustedProxies is { } proxies)
                {
                    context.EnvironmentVariables["UMBRAL__TRUSTED_PROXIES"] = proxies;
                }
            });
    }

    /// <summary>
    /// Sets the address people reach Umbral at, for example <c>https://auth.example.com</c>. It is the
    /// issuer in every token. Required before publishing.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="origin">Scheme, host and optional port, with no path. A trailing slash is removed.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="ArgumentException">The value is not an absolute http or https address, or has a path or query.</exception>
    public static IResourceBuilder<UmbralResource> WithPublicOrigin(this IResourceBuilder<UmbralResource> builder, string origin)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(origin);

        var trimmed = origin.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.PathAndQuery != "/"
            || uri.Fragment.Length > 0
            || trimmed.Contains('?', StringComparison.Ordinal)
            || trimmed.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{origin}' is not an origin. Give only the scheme, host and optional port, for example https://auth.example.com.",
                nameof(origin));
        }

        builder.Resource.PublicOrigin = trimmed;
        builder.Resource.PublicOriginParameter = null;
        return builder;
    }

    /// <summary>
    /// Sets the address people reach Umbral at from a parameter, for a deployment that learns it late.
    /// The value is not checked when publishing: Umbral refuses a bad one when it starts.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="origin">A parameter holding the origin, for example <c>https://auth.example.com</c>.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<UmbralResource> WithPublicOrigin(this IResourceBuilder<UmbralResource> builder, IResourceBuilder<ParameterResource> origin)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(origin);

        builder.Resource.PublicOriginParameter = origin.Resource;
        builder.Resource.PublicOrigin = null;
        return builder;
    }

    /// <summary>
    /// Sets <c>UMBRAL__TRUSTED_PROXIES</c>: the addresses and networks whose forwarded headers Umbral believes.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="proxies">One or more addresses or networks, for example <c>10.99.0.1</c> or <c>192.0.2.0/24</c>.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="ArgumentException">The list is empty, or an entry is empty or contains a comma.</exception>
    /// <remarks>An entry that is not an address or network is not checked here: Umbral names it when it starts.</remarks>
    public static IResourceBuilder<UmbralResource> WithTrustedProxies(this IResourceBuilder<UmbralResource> builder, params string[] proxies)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(proxies);

        if (proxies.Length == 0)
        {
            throw new ArgumentException("Give at least one address or network.", nameof(proxies));
        }

        foreach (var proxy in proxies)
        {
            if (string.IsNullOrWhiteSpace(proxy) || proxy.Contains(',', StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{proxy}' is not one address or network. Pass each entry as its own argument.", nameof(proxies));
            }
        }

        builder.Resource.TrustedProxies = string.Join(',', proxies.Select(p => p.Trim()));
        return builder;
    }

    /// <summary>
    /// Keeps Umbral's data (operator credential, connectors, apps, signing keys) in a named volume at <c>/data</c>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The volume name. Generated from the application and resource names when not given.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<UmbralResource> WithDataVolume(this IResourceBuilder<UmbralResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), DataPath);
    }

    /// <summary>
    /// Keeps Umbral's data in a folder of the host, mounted at <c>/data</c>. The folder must be writable by the container.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The folder on the host.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<UmbralResource> WithDataBindMount(this IResourceBuilder<UmbralResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, DataPath);
    }

    /// <summary>
    /// Gives an app <c>UMBRAL_ISSUER</c>, the issuer it validates tokens against.
    /// </summary>
    /// <typeparam name="TDestination">The type of the app.</typeparam>
    /// <param name="app">The app that reads Umbral.</param>
    /// <param name="umbral">Umbral.</param>
    /// <returns>The app's resource builder.</returns>
    /// <remarks>
    /// The value is Umbral's public origin, never its internal address (<c>http://umbral:8080</c>): an app
    /// compares the issuer with exactly what Umbral puts in tokens. No <c>services__</c> variable is added.
    /// </remarks>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> app,
        IResourceBuilder<UmbralResource> umbral)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(umbral);

        var resource = umbral.Resource;

        return app.WithEnvironment(async context =>
        {
            var origin = await UmbralOrigin.ResolveAsync(resource, context.ExecutionContext, context.CancellationToken).ConfigureAwait(false);
            context.EnvironmentVariables["UMBRAL_ISSUER"] = origin.Value;
        });
    }

    private static string DefaultImageTag() =>
        typeof(UmbralResourceBuilderExtensions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UmbralImageTag")?.Value
        ?? throw new InvalidOperationException("The assembly has no UmbralImageTag metadata.");
}
