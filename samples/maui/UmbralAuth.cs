using System.Security.Cryptography;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.IdentityModel.JsonWebTokens;

namespace UmbralSample;

/// <summary>One line of the private screen: a claim and its value.</summary>
public sealed record ClaimRow(string Name, string Value);

/// <summary>What a sign-in or a sign-out came to. <c>Message</c> is shown on the home screen.</summary>
public sealed record AuthOutcome(bool Succeeded, bool Cancelled, string? Message)
{
    public static AuthOutcome Ok(string? message = null) => new(true, false, message);
    public static AuthOutcome Cancel() => new(false, true, "Sign-in cancelled");
    public static AuthOutcome Fail(string message) => new(false, false, message);
}

/// <summary>
/// Everything the sample does with OidcClient, in one place.
///
/// OidcClient does the security-relevant work: it reads the endpoints from discovery, sends a PKCE
/// S256 challenge and a <c>state</c> and checks <c>state</c> when the browser comes back. We leave
/// that at its defaults. It does not send or check a <c>nonce</c> in the code flow, so this class
/// does. The <c>id_token</c> is validated by <see cref="UmbralIdentityTokenValidator"/>.
/// </summary>
public sealed class UmbralAuth
{
    // One slash after the colon: Umbral matches redirect URIs exactly.
    private const string RedirectUri = "com.cosmicchimps.umbral.sample.maui:/callback";
    private const string PostLogoutRedirectUri = "com.cosmicchimps.umbral.sample.maui:/signout-callback";

    // The claims the private screen shows, in this order. email and email_verified only when
    // Umbral sent them.
    private static readonly string[] ShownClaims = ["sub", "name", "idp", "email", "email_verified"];

    public const string SetClientIdMessage = "Set ClientId in UmbralSettings.cs, then build again.";

    public static UmbralAuth Shared { get; } = new();

    private StoredSession? session;
    private bool loaded;

    public static bool IsConfigured => !string.IsNullOrEmpty(UmbralSettings.ClientId);

    /// <summary>
    /// The claims to show when signed in, null when signed out. Signed in means: tokens are stored
    /// and the <c>id_token</c> has not expired. There is no refresh token (no
    /// <c>offline_access</c>), so an expired token ends the session locally and the next sign-in
    /// asks again.
    /// </summary>
    public async Task<IReadOnlyList<ClaimRow>?> GetClaimsAsync()
    {
        var current = await LoadSessionAsync();
        if (current is null)
        {
            return null;
        }
        if (IsExpired(current.IdToken))
        {
            Discard();
            return null;
        }
        return ShownClaims
            .Where(current.Claims.ContainsKey)
            .Select(name => new ClaimRow(name, current.Claims[name]))
            .ToList();
    }

    /// <summary>Discovers the endpoints, opens the browser, exchanges the code and keeps the tokens.</summary>
    public async Task<AuthOutcome> SignInAsync()
    {
        if (!IsConfigured)
        {
            return AuthOutcome.Fail(SetClientIdMessage);
        }
        if (CheckIssuer() is { } refused)
        {
            return AuthOutcome.Fail(refused);
        }

        // OidcClient's code flow sends no nonce and checks none, and the contract requires one:
        // send our own, and compare it with the id_token's below.
        var nonce = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var request = new LoginRequest
        {
            FrontChannelExtraParameters = new Parameters { { OidcConstants.AuthorizeRequest.Nonce, nonce } },
        };

        LoginResult result;
        try
        {
            result = await CreateClient().LoginAsync(request);
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            return AuthOutcome.Fail(Unreachable(ex));
        }

        if (result.IsError)
        {
            return result.Error == nameof(BrowserResultType.UserCancel)
                ? AuthOutcome.Cancel()
                : AuthOutcome.Fail(Describe("Sign-in failed", result.ErrorDescription ?? result.Error));
        }

        // LoginResult.User has no nonce claim (OidcClient filters it out), so read it from the
        // id_token itself, which has been validated by now.
        var idToken = new JsonWebToken(result.IdentityToken);
        var returned = idToken.TryGetPayloadValue<string>("nonce", out var value) ? value : null;
        if (returned is null || returned != nonce)
        {
            return AuthOutcome.Fail("Sign-in failed: the id_token's nonce does not match the request");
        }

        var claims = new Dictionary<string, string>();
        foreach (var name in ShownClaims)
        {
            if (result.User.FindFirst(name)?.Value is { } claim)
            {
                // A JSON boolean arrives as "True": show it as true or false, as the other samples do.
                claims[name] = claim is "True" or "False" ? claim.ToLowerInvariant() : claim;
            }
        }
        session = new StoredSession(result.IdentityToken, result.AccessToken, result.RefreshToken, claims);
        loaded = true;
        try
        {
            await TokenStore.SaveAsync(session);
        }
        catch (Exception ex)
        {
            // Signed in either way; a failed write only means it will not survive a restart.
            return AuthOutcome.Ok(Describe(
                "Signed in, but the secure store did not keep the tokens: you will be signed out when the app closes",
                ex.Message));
        }
        return AuthOutcome.Ok();
    }

    /// <summary>
    /// Signs out in the order the contract asks: end the app's own session first, then send the
    /// person to Umbral's end-session endpoint. Either way this device is signed out as soon as
    /// this is called; the message is only for an error at the end-session step.
    /// </summary>
    public async Task<string?> SignOutAsync()
    {
        var idToken = (await LoadSessionAsync())?.IdToken;
        Discard();
        if (idToken is null || !IsConfigured || CheckIssuer() is not null)
        {
            return null;
        }
        try
        {
            // Opens the end-session endpoint from discovery, with id_token_hint, the post-logout
            // redirect URI and a state.
            var result = await CreateClient().LogoutAsync(new LogoutRequest { IdTokenHint = idToken });
            // Cancelled or failed: we are signed out anyway; only a real error is shown.
            return result.IsError && result.Error != nameof(BrowserResultType.UserCancel)
                ? Describe("Sign-out at Umbral failed", result.ErrorDescription ?? result.Error)
                : null;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            return Unreachable(ex);
        }
    }

    private async Task<StoredSession?> LoadSessionAsync()
    {
        if (!loaded)
        {
            session = await TokenStore.LoadAsync();
            loaded = true;
        }
        return session;
    }

    private void Discard()
    {
        session = null;
        loaded = true;
        TokenStore.Clear();
    }

    private static OidcClient CreateClient()
    {
        var issuer = new Uri(UmbralSettings.Issuer);
        var options = new OidcClientOptions
        {
            // Endpoints come from /.well-known/openid-configuration only, never hard-coded.
            Authority = UmbralSettings.Issuer,
            ClientId = UmbralSettings.ClientId, // a public client: no secret
            Scope = "openid profile email",
            RedirectUri = RedirectUri,
            PostLogoutRedirectUri = PostLogoutRedirectUri,
            Browser = new WebAuthenticatorBrowser(),
            // Umbral's id_token carries the same claims as userinfo.
            LoadProfile = false,
            IdentityTokenValidator = new UmbralIdentityTokenValidator(),
        };
        // http only for a loopback issuer (a local Umbral); every other discovery check stays on.
        options.Policy.Discovery.RequireHttps = !(issuer.Scheme == Uri.UriSchemeHttp && issuer.IsLoopback);
        return new OidcClient(options);
    }

    /// <summary>https always; http only to a loopback address. Checked before any network call.</summary>
    private static string? CheckIssuer()
    {
        if (!Uri.TryCreate(UmbralSettings.Issuer, UriKind.Absolute, out var issuer) || string.IsNullOrEmpty(issuer.Host))
        {
            return "Issuer in UmbralSettings.cs is not a valid URL";
        }
        if (issuer.Scheme == Uri.UriSchemeHttps || (issuer.Scheme == Uri.UriSchemeHttp && issuer.IsLoopback))
        {
            return null;
        }
        return issuer.Scheme == Uri.UriSchemeHttp
            ? $"Refusing http:// for {issuer.Host}: https only (http for localhost)"
            : "Issuer in UmbralSettings.cs must start with https://";
    }

    // OidcClient throws when it cannot load the discovery document; the HTTP stack throws when the
    // token or end-session request cannot be sent.
    private static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException
        || (ex is InvalidOperationException && ex.Message.StartsWith("Error loading discovery document", StringComparison.Ordinal));

    private static string Describe(string what, string? detail) => $"{what}: {detail ?? "no details"}";

    // The detail goes on a second line.
    private static string Unreachable(Exception ex) => $"Could not reach Umbral\n{ex.Message}";

    private static bool IsExpired(string idToken) =>
        new JsonWebToken(idToken).ValidTo <= DateTime.UtcNow;
}
