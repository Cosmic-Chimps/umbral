using System.Text.Json;

namespace UmbralSample;

/// <summary>What the app keeps of a sign-in: the tokens and the claims it shows.</summary>
public sealed record StoredSession(
    string IdToken,
    string AccessToken,
    string? RefreshToken,
    IReadOnlyDictionary<string, string> Claims);

/// <summary>
/// Where the tokens are kept: <see cref="SecureStorage"/>, the Keychain on iOS and a
/// Keystore-backed store on Android. Never <c>Preferences</c> or a file.
/// </summary>
public static class TokenStore
{
    private const string IdTokenKey = "umbral.id_token";
    private const string AccessTokenKey = "umbral.access_token";
    private const string RefreshTokenKey = "umbral.refresh_token";
    private const string ClaimsKey = "umbral.claims";

    public static async Task SaveAsync(StoredSession session)
    {
        // Clear first, so a refresh token from an earlier sign-in cannot outlive this one.
        Clear();
        await SecureStorage.Default.SetAsync(IdTokenKey, session.IdToken);
        await SecureStorage.Default.SetAsync(AccessTokenKey, session.AccessToken);
        if (!string.IsNullOrEmpty(session.RefreshToken))
        {
            await SecureStorage.Default.SetAsync(RefreshTokenKey, session.RefreshToken);
        }
        await SecureStorage.Default.SetAsync(ClaimsKey, JsonSerializer.Serialize(session.Claims));
    }

    public static async Task<StoredSession?> LoadAsync()
    {
        try
        {
            var idToken = await SecureStorage.Default.GetAsync(IdTokenKey);
            var claimsJson = await SecureStorage.Default.GetAsync(ClaimsKey);
            if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(claimsJson))
            {
                return null;
            }
            var claims = JsonSerializer.Deserialize<Dictionary<string, string>>(claimsJson);
            if (claims is null)
            {
                return null;
            }
            var accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey) ?? "";
            var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            return new StoredSession(idToken, accessToken, refreshToken, claims);
        }
        catch (Exception)
        {
            // An unreadable store (for example, after a reinstall) counts as signed out.
            Clear();
            return null;
        }
    }

    /// <summary>Removes everything this app stored.</summary>
    public static void Clear()
    {
        SecureStorage.Default.Remove(IdTokenKey);
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        SecureStorage.Default.Remove(ClaimsKey);
    }
}
