### Added

- "Sign in with Apple": a built-in Apple connector. You enter the Services ID, the Team ID, the
  Key ID and the `.p8` key from Apple's developer site, and people can sign in with their Apple
  Account. Umbral signs Apple's client secret itself for each sign-in, from the key it keeps in
  the secret store, so there is no token to generate and nothing to renew every six months. The
  name Apple sends only the first time a person signs in is kept. Apple accepts only an https
  address on a real domain name as the return URL, and there is one Apple connector per
  instance. The button follows Apple's rules for it (its title, black or white) but does not
  carry Apple's logo yet. The walkthrough is `docs/connectors/apple.md`.

### Upgrade

- The database becomes schema version 10. An older image refuses to start on it: take a backup
  before you upgrade.
