using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// An Umbral OIDC server, run from its published container image.
/// </summary>
/// <remarks>
/// Deliberately not an <c>IResourceWithServiceDiscovery</c>: an app must validate the issuer against
/// the public origin Umbral puts in tokens, never against the internal address service discovery
/// would give it. Use <c>WithReference(umbral)</c> from this package instead.
/// </remarks>
/// <param name="name">The name of the resource.</param>
public class UmbralResource(string name) : ContainerResource(name)
{
    /// <summary>The name of the HTTP endpoint.</summary>
    public const string HttpEndpointName = "http";

    /// <summary>The port Umbral listens on inside the container.</summary>
    public const int ContainerPort = 8080;

    /// <summary>The port published on the host.</summary>
    public int Port { get; internal set; } = ContainerPort;

    /// <summary>The public origin set with a string, or <see langword="null"/>.</summary>
    public string? PublicOrigin { get; internal set; }

    /// <summary>The public origin set with a parameter, or <see langword="null"/>.</summary>
    public ParameterResource? PublicOriginParameter { get; internal set; }

    /// <summary>The trusted proxy addresses and networks, or <see langword="null"/> when none were set.</summary>
    public string? TrustedProxies { get; internal set; }
}
