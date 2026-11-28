using System;
using System.IO;
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
            // The first run: the setup token is in Umbral's console log, see the Aspire page of the docs.
            .WithUrlForEndpoint(UmbralResource.HttpEndpointName, _ => new ResourceUrlAnnotation { Url = "/admin/setup", DisplayText = "Claim (first run)" })
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
    /// <param name="origin">Scheme, host and optional port, with no path. It is stored in the form Umbral
    /// reports as its issuer: scheme and host in lowercase, no default port, no trailing slash.</param>
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

        // Umbral normalises its origin (PublicOrigin.fs: lowercase scheme and host, no default port)
        // and puts that in every token's iss. Apps get UMBRAL_ISSUER from this same value, so it is
        // stored in that form: "https://Auth.Example.com:443" would otherwise fail issuer validation.
        builder.Resource.PublicOrigin = uri.GetLeftPart(UriPartial.Authority);
        builder.Resource.PublicOriginParameter = null;
        return builder;
    }

    /// <summary>
    /// Sets the address people reach Umbral at from a parameter, for a deployment that learns it late.
    /// The value is not checked when publishing: Umbral refuses a bad one when it starts.
    /// </summary>
    /// <remarks>
    /// Apps receive the value as it is, as <c>UMBRAL_ISSUER</c>, so give it in the form Umbral reports as
    /// its issuer: lowercase, no default port, no trailing slash, for example <c>https://auth.example.com</c>.
    /// </remarks>
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
    /// <exception cref="InvalidOperationException">Something else is already mounted at <c>/data</c>:
    /// another volume, or <see cref="WithDataBindMount(IResourceBuilder{UmbralResource}, string)"/>. Calling it again with the same volume is fine.</exception>
    public static IResourceBuilder<UmbralResource> WithDataVolume(this IResourceBuilder<UmbralResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var volume = name ?? VolumeNameGenerator.Generate(builder, "data");

        return CheckDataMount(builder, ContainerMountType.Volume, volume, nameof(WithDataVolume))
            ? builder
            : builder.WithVolume(volume, DataPath);
    }

    /// <summary>
    /// Keeps Umbral's data in a folder of the host, mounted at <c>/data</c>. The folder must be writable by the container.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The folder on the host.</param>
    /// <returns>The resource builder.</returns>
    /// <exception cref="InvalidOperationException">Something else is already mounted at <c>/data</c>:
    /// another folder, or <see cref="WithDataVolume(IResourceBuilder{UmbralResource}, string?)"/>. Calling it again with the same folder is fine.</exception>
    public static IResourceBuilder<UmbralResource> WithDataBindMount(this IResourceBuilder<UmbralResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        // Aspire resolves a relative source against the AppHost's folder and stores that.
        var folder = Path.GetFullPath(source, builder.ApplicationBuilder.AppHostDirectory);

        return CheckDataMount(builder, ContainerMountType.BindMount, folder, nameof(WithDataBindMount))
            ? builder
            : builder.WithBindMount(source, DataPath);
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

        return WithIssuer(app, umbral.Resource);
    }

    /// <summary>Sets <c>UMBRAL_ISSUER</c>: one place for <c>WithReference(umbral)</c> and <c>WithReference(app)</c>.</summary>
    internal static IResourceBuilder<TDestination> WithIssuer<TDestination>(IResourceBuilder<TDestination> app, UmbralResource resource)
        where TDestination : IResourceWithEnvironment =>
        app.WithEnvironment(async context =>
        {
            var origin = await UmbralOrigin.ResolveAsync(resource, context.ExecutionContext, context.CancellationToken).ConfigureAwait(false);
            context.EnvironmentVariables["UMBRAL_ISSUER"] = origin.Value;
        });

    // Umbral has one data folder, /data. Aspire refuses a second, different bind mount on a target, but
    // lets a volume be added beside anything, which would leave two mounts at /data and the data in
    // whichever one the container runtime picks. So a conflict is refused here, naming both calls.
    // True when the same mount is already there (calling twice is a no-op, as Aspire's own is).
    private static bool CheckDataMount(IResourceBuilder<UmbralResource> builder, ContainerMountType type, string source, string call)
    {
        foreach (var mount in builder.Resource.Annotations.OfType<ContainerMountAnnotation>().Where(m => m.Target == DataPath))
        {
            if (mount.Type == type && mount.Source == source)
            {
                return true;
            }

            var existing = mount.Type == ContainerMountType.Volume ? nameof(WithDataVolume) : nameof(WithDataBindMount);

            throw new InvalidOperationException(
                $"Umbral keeps its data in one place, {DataPath}, and {existing} already put '{mount.Source}' there. {call} cannot also put '{source}' there: call only one of {nameof(WithDataVolume)} and {nameof(WithDataBindMount)}, with one value.");
        }

        return false;
    }

    private static string DefaultImageTag() =>
        typeof(UmbralResourceBuilderExtensions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UmbralImageTag")?.Value
        ?? throw new InvalidOperationException("The assembly has no UmbralImageTag metadata.");
}
