### Added

- Declare your apps and the passkey connector when Umbral starts, so there is nothing to click in
  the admin area. Set `UMBRAL__DECLARATION` (JSON) and one `UMBRAL__APP_SECRETS__<key>` per web app,
  in a compose file or any container environment. Umbral creates what is declared and updates it to
  match at every start. In the admin area a declared app and the passkey connector are marked
  **Declared** and shown read-only, with where to change them; branding and passkey invitations stay
  yours. An app you stop declaring is handed back to the admin area, never deleted. See
  "Declared apps" in the Aspire guide and the settings reference.
- The Aspire package declares for you: `WithPasskeys` declares the passkey connector, `AddApp`
  declares an app with a generated secret, and `WithReference(app)` gives a project its issuer,
  client ID and secret. `aspire run` then signs in with a new passkey with no admin step; claim
  Umbral only to open the admin area.

### Security

- A declared app's secret comes only from its own variable. Umbral stores a hash, never logs the
  value, and replaces the stored secret when the variable changes. Every change a declaration makes
  is a security event with the actor `system`, and none holds a secret or an address list.
- A declaration that conflicts with something made in the admin area (an app with the same client
  ID, a passkey connector set up with other settings) or is invalid is refused as a whole: Umbral
  stops with each problem in its log, and nothing is written. A passkey connector set up by hand
  with exactly the declared settings is adopted instead.
- The secrets are ordinary environment variables, visible to `docker inspect` and written to a
  compose publish's `.env` file. With open passkey sign-up, claim an instance before you make it
  reachable: until then anyone who reaches it can sign up to the declared apps.

### Upgrade

- The database moves to schema version 16. An older image refuses it, so take the backup first, as
  usual.
- Nothing changes unless `UMBRAL__DECLARATION` is set: with it unset, Umbral reads no declaration
  and your apps and connectors stay as they are.
