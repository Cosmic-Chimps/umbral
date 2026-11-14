# Contract: the Aspire package

`CosmicChimps.Aspire.Hosting.Umbral`, namespace `Aspire.Hosting`, beside a5pr's
`AddUmbral`.

## API

```csharp
public enum PasskeySignUp { InvitationsOnly, Open }
public enum UmbralAppType { Web, Spa }

public sealed class UmbralAppResource : Resource, IResourceWithParent<UmbralResource>
{
    public string Key { get; }               // the client ID
    public UmbralAppType Type { get; }
    public ParameterResource? ClientSecret { get; }   // Web only
}

// On Umbral
IResourceBuilder<UmbralResource> WithPasskeys(this IResourceBuilder<UmbralResource> umbral,
    PasskeySignUp signUp = PasskeySignUp.InvitationsOnly);

IResourceBuilder<UmbralAppResource> AddApp(this IResourceBuilder<UmbralResource> umbral,
    string key, UmbralAppType type = UmbralAppType.Web);

// On an app
IResourceBuilder<UmbralAppResource> WithDisplayName(this IResourceBuilder<UmbralAppResource> app, string name);
IResourceBuilder<UmbralAppResource> WithRedirectUri(this IResourceBuilder<UmbralAppResource> app, string uri);
IResourceBuilder<UmbralAppResource> WithPostLogoutRedirectUri(this IResourceBuilder<UmbralAppResource> app, string uri);
IResourceBuilder<UmbralAppResource> WithCallbackPaths(this IResourceBuilder<UmbralAppResource> app,
    string signIn = "/signin-oidc", string? signOut = "/signout-callback-oidc");
IResourceBuilder<UmbralAppResource> WithConnectors(this IResourceBuilder<UmbralAppResource> app, params string[] slugs);
IResourceBuilder<UmbralAppResource> WithClientSecret(this IResourceBuilder<UmbralAppResource> app,
    IResourceBuilder<ParameterResource> secret);

// On the project that signs in through the app
IResourceBuilder<T> WithReference<T>(this IResourceBuilder<T> destination,
    IResourceBuilder<UmbralAppResource> app) where T : IResourceWithEnvironment;
```

## Behaviour

- `AddApp(key)` checks the key against `[a-z0-9-]{1,32}` and throws on a bad
  or repeated one. The resource is named `<umbral>-<key>` (so the project can
  be named `key`), is a child of Umbral in the dashboard, and has no lifecycle
  of its own.
- A `Web` app gets a secret parameter `<umbral>-<key>-client-secret`:
  `AddParameter(name, new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true)`
  (48 characters, well over Umbral's 32, pinned by a test, N2).
  In run mode Aspire keeps the generated value in the AppHost's user secrets,
  so it is the same at every run and never in the repository.
  `WithClientSecret` replaces it with the developer's own parameter.
- `WithPasskeys` and the first `AddApp` add to Umbral the environment of
  `contracts/declaration.md`: `UMBRAL__DECLARATION` (built from the model at
  start, plain JSON) and `UMBRAL__APP_SECRETS__<key>` with `-` written as `_`
  (N1; a reference to the
  secret parameter, never its value). Without either call Umbral gets neither
  variable.
- Redirect URIs: the ones given with `WithRedirectUri`, plus, when
  `WithCallbackPaths` applies (by default for a `Web` app with no explicit
  redirect URI), the referencing resource's `http` endpoint followed by the
  paths. Endpoint-derived URIs are refused in publish mode with an exception
  that says to use `WithRedirectUri` with the public address. An app with no
  redirect URI at start is an error naming the app.
- `WithReference(app)` on a project sets `UMBRAL_ISSUER` (the public origin, as
  `WithReference(umbral)`), `UMBRAL_CLIENT_ID` (the key) and, for a `Web` app,
  `UMBRAL_CLIENT_SECRET` (the parameter reference). It records the project as
  the app's referrer for `WithCallbackPaths`. An app referenced by two projects
  is an error unless its redirect URIs are explicit.
- The manifest shows `UMBRAL__DECLARATION` in plain text (no secret) and the
  secrets as `{<umbral>-<key>-client-secret.value}`.
- Claiming: the Umbral resource gets a second URL, "Claim (first run)", to
  `/admin/setup`. The setup token stays in Umbral's console log, where a5pr's
  docs point.
