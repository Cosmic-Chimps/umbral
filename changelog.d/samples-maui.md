### Added

- Samples: a .NET MAUI sample in `samples/maui/`: one C# project for Android and iOS signing in
  through Umbral with `Duende.IdentityModel.OidcClient` and MAUI's `WebAuthenticator` as a
  **Single-page or native app**, with a custom-scheme redirect URI, tokens in `SecureStorage`
  and sign-out through Umbral. CI builds the Android target; its sign-in is checked by hand.
