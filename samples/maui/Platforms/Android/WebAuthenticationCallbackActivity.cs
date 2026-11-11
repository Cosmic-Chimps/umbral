using Android.App;
using Android.Content;
using Android.Content.PM;

namespace UmbralSample;

/// <summary>
/// Catches the redirect to <c>com.cosmicchimps.umbral.sample.maui:/callback</c> (and
/// <c>:/signout-callback</c>) and hands it to <c>WebAuthenticator</c>, which closes the Custom Tab.
/// Change <c>DataScheme</c> together with the two URIs in <c>UmbralAuth.cs</c> and
/// <c>CFBundleURLSchemes</c> in <c>Platforms/iOS/Info.plist</c> (README, "Your own app").
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.cosmicchimps.umbral.sample.maui")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
