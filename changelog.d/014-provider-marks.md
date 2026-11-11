### Added

- Apple's logo on the "Sign in with Apple" button. Apple's licence for its logo artwork forbids
  redistributing it, so Umbral cannot ship it: you paste the SVG you download from Apple Design
  Resources into the new **Sign-in button logo** section of the Apple pane, and the button shows
  it. Umbral keeps only the logo's outline and refuses a file with anything else in it. Apple's
  rules and App Review expect the logo, so add it before you submit an app that offers Sign in
  with Apple. See step 4 of `docs/connectors/apple.md`.

### Changed

- The sign-in page's GitHub button now shows the GitHub logo, in white or black as GitHub's
  logo rules require, and the passkey button shows a key. Google, Discord, Fediverse and
  OpenID Connect buttons keep the link icon: Google's rules allow its "G" only on Google's own
  button colours and typeface, and Discord's terms do not cover a sign-in button.

### Upgrade

- The database becomes schema version 11. An older image refuses to start on it: take a backup
  before you upgrade.
