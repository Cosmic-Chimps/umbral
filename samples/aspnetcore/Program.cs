using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"The environment variable {name} is required.");

var issuer = Required("UMBRAL_ISSUER");
var clientId = Required("UMBRAL_CLIENT_ID");
var clientSecret = Required("UMBRAL_CLIENT_SECRET"); // a Web application is a confidential client
var port = Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } p ? p : "5000";

// Plain http is allowed on a loopback address only, the one place Umbral
// accepts it (the quick start's http://localhost:8080). Everywhere else the
// issuer is https and so is the app.
var plainHttpOnLoopback =
    Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) && issuerUri.Scheme == "http" && issuerUri.IsLoopback;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{port}");

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // Secure always, so a proxy that terminates TLS and speaks http to the
        // app cannot leave the cookie without it; only the plain-http loopback
        // trial goes without.
        o.Cookie.SecurePolicy = plainHttpOnLoopback ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    })
    .AddOpenIdConnect(o =>
    {
        // Discovery only: the endpoints are read from the issuer, never set by hand.
        o.Authority = issuer;
        o.RequireHttpsMetadata = !plainHttpOnLoopback; // the handler wants https, except for that trial
        o.ClientId = clientId;
        o.ClientSecret = clientSecret;
        o.ResponseType = "code";
        o.UsePkce = true; // Umbral requires PKCE
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        o.MapInboundClaims = false; // so sub, name and idp keep their names
        o.TokenValidationParameters.NameClaimType = "name";
        o.GetClaimsFromUserInfoEndpoint = false; // Umbral's id_token already carries the same claims as userinfo
        // Sign-out sends the saved id_token as id_token_hint, which the end-session
        // request uses to name the app whose post-logout redirect URI it asks for.
        o.SaveTokens = true;
        o.SignedOutRedirectUri = "/";
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

static IResult Page(string title, string body) =>
    Results.Content(
        $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{title}</title></head><body>{body}</body></html>",
        "text/html");

app.MapGet("/", (HttpContext http) =>
{
    var session = http.User.Identity?.IsAuthenticated == true
        ? "<form method=\"post\" action=\"/signout\"><button type=\"submit\">Sign out</button></form>"
        : "<a href=\"/signin\">Sign in</a>";
    return Page("Umbral sample: ASP.NET Core",
        $"<h1>Umbral sample: ASP.NET Core</h1><p><a href=\"/public\">Public</a> <a href=\"/private\">Private</a></p><p>{session}</p>");
});

app.MapGet("/public", () =>
    Page("Public", "<h1>Public</h1><p>Anyone can read this page, signed in or not.</p><p><a href=\"/\">Back</a></p>"));

app.MapGet("/private", (HttpContext http) =>
{
    var rows = new System.Text.StringBuilder();
    foreach (var type in new[] { "sub", "name", "idp", "email", "email_verified" })
        if (http.User.FindFirst(type) is { } claim)
            rows.Append($"<tr><th>{type}</th><td>{WebUtility.HtmlEncode(claim.Value)}</td></tr>");
    return Page("Private", $"<h1>Private</h1><table>{rows}</table><p><a href=\"/\">Back</a></p>");
}).RequireAuthorization();

app.MapGet("/signin", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapPost("/signout", () =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

app.Run();
