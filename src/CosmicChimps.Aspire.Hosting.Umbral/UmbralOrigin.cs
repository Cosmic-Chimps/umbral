using System;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// The public origin of an Umbral resource and the rules that follow from it. One place, so the
/// container's own environment and every app's <c>UMBRAL_ISSUER</c> cannot disagree.
/// </summary>
internal static class UmbralOrigin
{
    /// <summary>The origin to hand out, and whether it is the plain-http laptop case.</summary>
    /// <param name="Value">A string, or a <see cref="ParameterResource"/> (publish mode only).</param>
    /// <param name="LaptopPair">True only for <c>http://</c> with a loopback host.</param>
    internal readonly record struct Resolved(object Value, bool LaptopPair);

    internal static async ValueTask<Resolved> ResolveAsync(
        UmbralResource umbral, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        if (executionContext.IsRunMode)
        {
            var origin = umbral.PublicOriginParameter is { } parameter
                ? await parameter.GetValueAsync(cancellationToken).ConfigureAwait(false)
                : umbral.PublicOrigin ?? $"http://localhost:{umbral.Port}";

            var laptop = IsLoopbackHttp(origin!);

            // A plain-http localhost origin is the endpoint itself: on another port, the issuer
            // would name an address nothing listens on.
            if (laptop && new Uri(origin!).Port != umbral.Port)
            {
                throw new InvalidOperationException(
                    $"Umbral resource '{umbral.Name}' listens on port {umbral.Port}, but its public origin '{origin}' names another port. "
                    + $"Give AddUmbral(\"{umbral.Name}\", port: {new Uri(origin!).Port}), or leave WithPublicOrigin out to use http://localhost:{umbral.Port}.");
            }

            return new Resolved(origin!, laptop);
        }

        // Publish mode never sets the laptop pair and never guesses an origin.
        if (umbral.PublicOriginParameter is { } published)
        {
            // Its value is not known at publish. Umbral refuses plain http on a non-loopback host
            // when it starts, and without the pair it refuses http entirely, so a wrong value
            // fails at start and never runs insecurely.
            return new Resolved(published, false);
        }

        if (umbral.PublicOrigin is null)
        {
            throw new InvalidOperationException(
                $"Umbral resource '{umbral.Name}' has no public origin. Call WithPublicOrigin(...) with the https address people reach it at before publishing.");
        }

        if (!umbral.PublicOrigin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Umbral resource '{umbral.Name}' has the public origin '{umbral.PublicOrigin}', which must be https in a published app. Call WithPublicOrigin(...) with the https address people reach it at.");
        }

        return new Resolved(umbral.PublicOrigin, false);
    }

    /// <summary>Umbral's own rule: plain http is accepted only for localhost, 127.0.0.1 and [::1].</summary>
    internal static bool IsLoopbackHttp(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Host.ToLowerInvariant() is "localhost" or "127.0.0.1" or "[::1]" or "::1";
}
