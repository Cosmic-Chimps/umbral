namespace UmbralSample;

/// <summary>
/// The two settings of the sample, and the one file to edit. They are built into the app, so build
/// again after changing them. There is no secret (a public client: PKCE proves it is the app) and
/// no port.
/// </summary>
public static class UmbralSettings
{
    /// <summary>The Issuer URL on the app's page in Umbral.</summary>
    public const string Issuer = "http://localhost:8080";

    /// <summary>The app's Client ID. There is no secret: a public client.</summary>
    public const string ClientId = "";
}
