using Duende.IdentityModel.OidcClient.Browser;
using IBrowser = Duende.IdentityModel.OidcClient.Browser.IBrowser;

namespace UmbralSample;

/// <summary>
/// The system's sign-in browser for OidcClient: a Custom Tab on Android and
/// <c>ASWebAuthenticationSession</c> on iOS, both through MAUI's <see cref="WebAuthenticator"/>.
/// Never a WebView: the app cannot read what is typed into Umbral's page.
/// </summary>
public sealed class WebAuthenticatorBrowser : IBrowser
{
    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(options.StartUrl),
                CallbackUrl = new Uri(options.EndUrl),
                // false: share the system browser's cookies, so a person already signed in to
                // Umbral is not asked again, as the Android and iOS samples do.
                PrefersEphemeralWebBrowserSession = false,
            });

            // WebAuthenticator hands back the parameters of the redirect; OidcClient wants the URL.
            var query = string.Join("&", result.Properties.Select(
                p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            return new BrowserResult
            {
                ResultType = BrowserResultType.Success,
                Response = $"{options.EndUrl}?{query}",
            };
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException is a kind of this: the person closed the sheet.
            return new BrowserResult { ResultType = BrowserResultType.UserCancel };
        }
    }
}
