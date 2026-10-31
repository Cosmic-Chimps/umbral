### Added

- You can add passkeys to the operator credential, up to 10, on the **Credential** page, and sign
  in to the admin area with **Sign in with a passkey**. They need no connector, never appear on
  **Users**, and never sign anyone in to an app.
- You can turn password sign-in off once you have a passkey. While it is off, no password is
  accepted, and the stored password comes back unchanged when you turn it on again.
- `reset-credential --forget-passkeys` also removes every operator passkey, for a stolen device.

### Changed

- The **Credential** page has three parts: **Passkeys**, **Password sign-in** and **Password**.
- `reset-credential` turns password sign-in back on and keeps the operator passkeys. Its output
  says "password", not "credential".

### Upgrade

- The database becomes schema version 8. An older image refuses to start on it: take a backup
  before you upgrade. Nothing else changes until you add a passkey: there is none, and password
  sign-in stays on.
