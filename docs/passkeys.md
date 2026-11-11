# Passkeys

A passkey is a key that a person's phone, laptop or security key holds, unlocked
by their fingerprint, face or PIN. Nothing is typed and there is nothing to
phish, and no third party learns who signed in. Umbral keeps only the public
half, which is not a secret. In Umbral a passkey is a connector (Umbral itself
is the provider), and a person's passkeys belong to their user. Details and
gotchas are in the [Passkeys walkthrough](connectors/passkey.md).

Umbral uses passkeys in two places, and they are separate:

- **For the people who sign in to your apps**, through the passkey connector. The walkthrough
  covers enabling it, inviting someone, signing in, adding a passkey to an account that signs in
  with a provider, recovery, removing a passkey and what an app receives.
- **For you, in the admin area.** See [Passkeys for the admin area](#passkeys-for-the-admin-area)
  below.

**The security log.** The events are `passkey_registered`, `passkey_removed`,
`passkey_invitation_created` and `passkey_invitation_revoked`, and a refused
passkey sign-in or invitation is a `sign_in_failed` event whose detail starts
`passkey_refused;reason=`. No challenge, credential id, key or invitation link is
ever logged.

**Memory.** An instance with the passkey connector in use idles under 160 MB (a
ceiling, as with an external secret store; the figure is confirmed by the
idle-memory test). Without the connector it stays under 150 MB.

## Passkeys for the admin area

You can also sign in to the admin area with a passkey. These passkeys are the
operator's own, not people's. They do not need the passkey connector to be
enabled, they never appear on **Users**, and they never sign anyone in to an
app.

Open **Credential** in the side rail. The page has three parts: **Passkeys**,
**Password sign-in** and **Password**.

- **Passkeys.** Type an optional name, then click **Add a passkey** and
  confirm on the device. You can add up to 10. Each one is listed with when it
  was added and when it was last used, and a **Remove** button.
- **Password sign-in.** It is **On** until you turn it off. **Turn password
  sign-in off** works only once you have a passkey, and asks you to confirm
  with a passkey. While it is off, no password is accepted anywhere, the sign-in
  page shows only the passkey, and the **Password** part is hidden. Your
  password is kept, and comes back unchanged when you click **Turn password
  sign-in on**.
- **Password.** Replaces the password, as before.

**Signing in.** On `/admin/signin`, click **Sign in with a passkey** and pick
the passkey on your device. No name is typed. The password form stays beside it
while password sign-in is on. Without JavaScript the page shows the password
form, or, while password sign-in is off, says that a passkey needs JavaScript.
Adding a passkey needs JavaScript.

**Asking again.** Adding a passkey and removing one each ask you to confirm
first, with your current password (while password sign-in is on) or with a
passkey. Turning password sign-in on or off asks for a passkey. Removing a
passkey and turning password sign-in off sign out every other admin session.
You cannot remove the last passkey while password sign-in is off: turn password
sign-in on first, or add another passkey.

**Bound to the host name.** Like people's passkeys, the operator's belong to the
host name in `UMBRAL__PUBLIC_ORIGIN`. If you change the host name, they stop
working; a change of scheme or port does not.

**If you lose the device.** Run `reset-credential` on the host. It keeps the
passkeys unless you add `--forget-passkeys` (see
[Forgotten operator credential](run/operator-credential.md)).

**The security log.** Adding a passkey is `operator_passkey_added`, removing one
is `operator_passkey_removed`, and a change of the switch is
`operator_password_signin_changed`, with the detail `on` or `off`
(`offline_reset` when `reset-credential` turned it back on); a refused change
is the same event with a failure outcome. A passkey sign-in
is an `operator_signed_in` event with the detail `passkey`. A refused one is an
`operator_sign_in_failed` event whose detail starts `passkey_refused;reason=`.
No challenge, credential id, key or password is ever logged.
