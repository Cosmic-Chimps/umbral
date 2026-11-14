### Added

- A NuGet package, `CosmicChimps.Aspire.Hosting.Umbral`, that runs Umbral from a .NET Aspire
  AppHost with `builder.AddUmbral("umbral")`. Its version is the image tag it runs, never `latest`.
  It fixes the port (8080 unless given), sets the laptop settings for `aspire run`, and refuses to
  publish without an `https://` address from `WithPublicOrigin`. See "Run Umbral from an Aspire
  AppHost" in the documentation.
