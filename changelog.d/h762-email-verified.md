### Security
- An email address a person never proved could be marked as verified, and the real owner of that
  address could then be joined into that person's account. Umbral keeps the first address a
  provider gives for a user, but it set `email_verified` whenever the provider verified *any*
  address. So someone could sign in through Discord with another person's address, unverified,
  then change their Discord address to one of their own and verify it. Umbral then sent apps the
  first address with `email_verified: true`. When the real owner of that address later signed in
  through another provider that verifies it, Umbral linked them into the first person's user,
  which could then sign in to every app as them. `email_verified` now becomes `true` only when a
  provider verifies the address Umbral stores for the user. Every version from 0.1.0 has this.

### Upgrade
- The database becomes schema version 9. An older image refuses to start on it: take a backup
  before you upgrade.
- On its first start, Umbral sets `email_verified` back to `false` for every user whose stored
  address no linked provider account asserts as verified today, and logs how many users that
  was, as a warning when it is not 0. Nobody is linked to those users by email any more, and
  apps see `email_verified: false` for them until a provider verifies that address again.
- Joins that already happened are not undone. To look for one, run the query in the README's
  Troubleshooting entry **A user may hold someone else's provider account**.
