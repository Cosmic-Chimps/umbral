# Start over: clear every user

Run this on the machine that hosts the container:

```bash
docker exec umbral /app/Umbral clear-users --yes
# compose:
docker compose exec umbral /app/Umbral clear-users --yes
```

It deletes every person Umbral knows, and keeps all your setup. It runs in the
running container, beside the live process: no shell to open, no restart, no
downtime. On a Caravana host, run it from the host with `docker exec`, as above.

**There is no undo.** Take a [backup](backup.md) first.

Use it to start over after you tried an instance with real providers and before
you open it, to empty a staging copy, or, run on a schedule, to empty a public
test instance every night.

## Look first

Without `--yes` it changes nothing:

```bash
docker exec umbral /app/Umbral clear-users
```

```
This would delete 12 users and everything they hold. Nothing was changed. Run it again with --yes to delete them (add --security-log to empty the security log too).
```

It exits with code 2, so a script that forgot `--yes` stops, and can tell that
from a clearing that failed (code 1).

## What goes and what stays

In one transaction, it deletes:

- every user;
- their linked provider accounts;
- their passkeys, every passkey invitation (used or not: if you want one again,
  make a new link), and every passkey ceremony in progress;
- the records of the provider accounts you removed from a user (see
  [When a link is removed](../integrate.md#when-a-link-is-removed)). Umbral
  stops treating those accounts as strangers: one whose provider still marks an
  unconfirmed address as verified can be joined, by email, to the person who
  signs in again with that address. Before you clear, set any connector you
  removed a link from to **Not trusted** (see
  [Changing the email setting later](../connectors/oidc.md#changing-the-email-setting-later));
- every authorization and token Umbral issued. All of them belong to people;
  the operator has none.

It keeps:

- the operator password and the operator's passkeys;
- the connectors, including the Fediverse servers Umbral registered with;
- the apps, with their branding, redirect URIs and chosen connectors;
- what a [declaration](aspire.md) owns;
- the settings, the token signing and encryption keys, and the data protection
  keys.

So the instance works as before, with nobody in it. You do not sign in to the
admin area again, and no app needs a new secret.

## The security log

The log is kept. Add `--security-log` to empty it too:

```bash
docker exec umbral /app/Umbral clear-users --yes --security-log
```

Either way, one event is then recorded: `users_cleared`, with the actor `system`
and the detail `users=<n>`, or `users=<n>;security_log=cleared` when you emptied
the log. The log always shows that a clearing happened, and when. It never
records a name, an address or a subject.

## What you see

```
Deleted 12 users, 15 linked accounts, 4 passkeys, 93 tokens.
Security log cleared.
```

The second line is printed only when you asked for it. The exit code is 0.

If something goes wrong, Umbral rolls back, prints the reason and exits with
code 1. Nothing was changed, and you can run it again.

## What people and apps notice

- **A person signed in to an app** is signed out: the next time that app sends
  them to Umbral, it gets `error=login_required` back, as for a
  [deleted person](../integrate.md#when-a-person-is-disabled-or-deleted). A
  refresh token is answered with `invalid_grant`, and an access token gets `401`
  at `/connect/userinfo`.
- **A person who signs in again is a new user, with a new `sub`**, even with the
  same provider account and the same email address. An app that keyed its own
  accounts on `sub` sees a new account. Clear that app's own accounts too, or
  expect duplicates.
- **A passkey a person made is gone.** They register a new one, or, when the
  passkey connector takes invitations only, wait for a new invitation from you.
