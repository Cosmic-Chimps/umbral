### Added

- An app can have up to 10 redirect URIs and up to 10 post-logout redirect URIs,
  one for each place it runs (your machine, staging, production). Add and remove
  them on the app's page on the **Apps** screen. The client ID and the secret
  stay the same, so there is no need to delete and register an app again to
  change its redirect URI. Each one is still matched exactly, an app keeps at
  least one redirect URI, and every addition and removal is written to the
  security log.
