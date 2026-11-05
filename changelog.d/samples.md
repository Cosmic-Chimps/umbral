### Added

- Samples: `samples/` has one small app per stack that signs in through Umbral with the
  stack's ordinary OpenID Connect library, starting with ASP.NET Core. Every sample has the
  same three pages (home, public, private with the claims) and signs out through Umbral, and
  CI runs each one against a real Umbral in a headless browser before every release.
