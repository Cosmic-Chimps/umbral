### Added

- "Sign in with Bluesky": a built-in Bluesky connector. People type their handle and sign in
  at their own server, whether Bluesky hosts the account or it is a self-hosted AT Protocol
  server. There is nothing to create at any provider: you turn the connector on, and choose
  **Any server** or **Only these servers**. Apps receive `name`, `preferred_username` (the
  handle), `picture` and `profile`, and no email; a person who changes handle stays the same
  user, because Umbral identifies them by their DID. It needs Umbral at an https address
  without a port that servers can reach from the internet; the pane says so otherwise.
  There is one Bluesky connector per instance. The walkthrough is
  `docs/connectors/bluesky.md`.

### Upgrade

- The database becomes schema version 12, which adds one nullable column. It is automatic and
  needs no action. An older image refuses to start on it: take a backup before you upgrade.
