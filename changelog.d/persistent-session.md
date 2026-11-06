### Changed

- A person's Umbral session now survives the browser closing: its cookie is persistent and
  lasts 14 days from the last use, as the session already did. Signing in to a second app
  after a browser restart no longer shows Umbral's page again, and a second app on the same
  phone can now find the session the first one left. Closing the browser no longer signs anyone out: on a shared computer, people
  sign out at `/connect/logout`. Disabling or deleting someone still ends their session at
  their next sign-in. A session from before the upgrade keeps its browser-session cookie
  until the person next signs in through a connector.
