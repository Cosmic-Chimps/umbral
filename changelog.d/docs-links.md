### Changed

- The "Documentation" link at the foot of every page opens the documentation site,
  <https://cosmic-chimps.github.io/umbral/>, in place of the README on GitHub.
- A message about a secret store that fails (the warning at startup, the notice on Connectors, a
  save that is refused, a connector whose secret cannot be read) gives the address of that store's
  page on the documentation site. It used to say `docs/secret-stores/`, a folder the image does
  not have.

### Fixed

- The warning the upgrade to schema version 9 writes to the log, when it sets `email_verified`
  back to false for some users, gives the address of the Troubleshooting page. It named a
  Troubleshooting section of the README, which moved to the documentation site.
