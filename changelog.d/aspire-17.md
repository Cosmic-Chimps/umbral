### Changed

- The Aspire hosting package, `CosmicChimps.Aspire.Hosting.Umbral`, and the Aspire
  sample now use Aspire 17.0.0. `WithDataVolume` and `WithDataBindMount` now refuse a
  second, different mount at `/data` (another volume or folder, or the other method)
  with a message that names both calls, where Aspire alone would have mounted both.

### Upgrade

- An AppHost that uses `CosmicChimps.Aspire.Hosting.Umbral` from this release on needs
  Aspire 17: the AppHost project uses `Microsoft.NET.Sdk` with the
  `Aspire.Hosting.AppHost` 17 package (the `Aspire.AppHost.Sdk` project SDK is
  obsolete), as Aspire's own migration notes describe. An AppHost still on Aspire 13
  keeps using the 0.13.x package.
