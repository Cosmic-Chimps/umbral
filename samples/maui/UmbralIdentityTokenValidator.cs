using System.Security.Claims;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Results;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace UmbralSample;

/// <summary>
/// Checks the <c>id_token</c> the way an app that has no server should: its signature against the
/// keys of Umbral's discovery document, its issuer, its audience (this app's client ID) and its
/// lifetime. OidcClient's default validator does the same with the same library; this one is here
/// to keep the claim names as Umbral sends them (<c>sub</c>, <c>name</c>, <c>idp</c>, ...).
/// </summary>
public sealed class UmbralIdentityTokenValidator : IIdentityTokenValidator
{
    // The signature algorithms of public keys. A token signed with a shared secret, or not signed
    // at all, is refused.
    private static readonly string[] Algorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    public async Task<IdentityTokenValidationResult> ValidateAsync(
        string identityToken, OidcClientOptions options, CancellationToken cancellationToken = default)
    {
        var provider = options.ProviderInformation;
        var keys = new List<SecurityKey>();
        foreach (var key in provider.KeySet?.Keys ?? [])
        {
            keys.Add(new Microsoft.IdentityModel.Tokens.JsonWebKey
            {
                Kty = key.Kty,
                Kid = key.Kid,
                Use = key.Use,
                Alg = key.Alg,
                N = key.N,
                E = key.E,
                X = key.X,
                Y = key.Y,
                Crv = key.Crv,
            });
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = provider.IssuerName,
            ValidateAudience = true,
            ValidAudience = options.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidAlgorithms = Algorithms,
        };

        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var result = await handler.ValidateTokenAsync(identityToken, parameters);
        if (!result.IsValid)
        {
            // OidcClient answers "invalid_signature" by fetching the keys again (a rotated key)
            // and validating once more.
            var error = result.Exception is SecurityTokenSignatureKeyNotFoundException
                or SecurityTokenInvalidSignatureException
                ? "invalid_signature"
                : result.Exception?.Message ?? "The id_token is not valid";
            return new IdentityTokenValidationResult { Error = error };
        }

        return new IdentityTokenValidationResult
        {
            User = new ClaimsPrincipal(result.ClaimsIdentity),
            SignatureAlgorithm = ((JsonWebToken)result.SecurityToken).Alg,
        };
    }
}
