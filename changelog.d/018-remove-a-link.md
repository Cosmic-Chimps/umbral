### Added

- **Remove link** on the **Users** screen removes one provider account from a user, without
  disabling or deleting anyone. The person who stays keeps their account, their `sub` and their
  sessions. The removed account is signed out everywhere and becomes a separate user at its next
  sign-in. A user with two or more ways to sign in lists them under **Linked accounts**, with the
  date each was linked and last used. A user's last way to sign in can't be removed.

### Changed

- Removing a passkey, by its owner or on **Users**, now also signs out whatever was signed in
  with it: the Umbral session and its refresh tokens are refused at their next use.

### Security

- A provider account joined to the wrong person (a provider that marks unchecked addresses as
  verified, a reused company address, a shared inbox) can now be removed from that person alone.
  Its sessions and refresh tokens are refused at their next use. The passkeys added while signed
  in through it go with it. Umbral never trusts that account's addresses again, so it can't be
  joined to anyone by email. See "When a link is removed" in the integration guide.
- The passkey pages (`/account/passkeys` and adding a passkey) now check the session as an app
  sign-in does: a session whose sign-in method was removed, or whose connector was deleted or
  replaced, is signed out there too.

### Upgrade

- The database moves to schema version 15. An older image refuses it, so take the backup first,
  as usual.
- Sessions and refresh tokens from before the upgrade don't record which provider account or
  passkey they were made with. Once you remove a link or a passkey from someone, that person's
  Umbral sessions from before the upgrade, and the refresh tokens your apps hold for them from
  before the upgrade, end at their next use, and they sign in again once.
