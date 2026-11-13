### Added

- An OpenID Connect provider now has a setting, **Verified email addresses from this provider**:
  **Trusted** or **Not trusted**. With **Not trusted**, Umbral still stores the address and gives
  it to your apps, but always as unverified, so it never links a person to an existing user with
  the same address and never meets an allowed email domains rule. A new provider needs a choice,
  with none preselected. Changing it applies from the next sign-in and unlinks nobody.

### Security

- Some providers, such as Forgejo and Gitea, mark the address of every active account as
  verified, even one nobody confirmed. Set to **Not trusted**, a provider like that is safe to
  add whoever can register there. Turning trust off also makes unverified the stored addresses
  that only this provider verified, so an account a stranger registered earlier no longer holds
  someone else's address as verified. The page asks first and says how many.

### Upgrade

- The database moves to schema version 14. An older image refuses it, so take the backup first,
  as usual.
- Every existing OpenID Connect provider is set to **Trusted**, so nobody's sign-in changes.
  Review the setting on each one, under **Connectors**.
