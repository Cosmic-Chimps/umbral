# Troubleshooting

Commands below use the quick-start container name. With compose, write
`docker compose logs umbral` for `docker logs umbral`.

**The container exits right after starting.**
`docker logs umbral`: Umbral names the setting. Usual causes:
`UMBRAL__PUBLIC_ORIGIN` is missing, has a path (`https://auth.example.com/auth`)
or is `http://` with a host other than `localhost`; `UMBRAL__TRUSTED_PROXIES`
holds an entry that is not an IP address or network; or `/data` is not writable. For a bind mount, run
`chown -R 1654:1654 ./data`.

**The passkey button does nothing, or the page asks me to turn on JavaScript.**
Passkeys need JavaScript on the sign-in, invitation and passkeys pages. Turn it
on for this page and reload, or choose another way to sign in. A browser without
passkey support behaves the same way. Passkeys also need HTTPS (or `localhost`).

**An invitation link says it can't be used.**
The link was already used, ran out (7 days), was revoked, or its account was
disabled or deleted. Ask for a new one. The cause is in `security_events` as
`passkey_refused;reason=invitation_…`.

**I added a connector and nobody sees it.**
A connector that is **not shared** and that **no app has chosen** is offered
nowhere. The connector's page says so. Either tick **Shared with every app** on
its page, or open **Apps**, set an app's **Sign-in methods** to **Only the
connectors I choose**, tick the connector and click **Save sign-in methods**. A
disabled connector is not offered either.

**An app's sign-in page says no sign-in method is available.**
That app has no enabled connector it can offer: its chosen set is empty, or every
connector in it is disabled or deleted, or it is on **All shared connectors** and
none is shared and enabled. Fix it on **Apps** (**Sign-in methods**) or on
**Connectors**.

**Someone signed in, but the app shows its sign-in page again.**
A session counts for an app only if it came through a connector that app offers.
They signed in to Umbral through a connector this app does not offer, so they
must pick one it does. They are still signed in to Umbral for other apps.

**I cannot find the setup token, or it is rejected.**
`docker logs umbral 2>&1 | grep -A6 "not configured yet"` and take the *latest*
`Token` line (the `=====` box near the end of the log). The token is regenerated
each time the container starts, and only matters until you set the credential.
If the credential is already set, use `/admin/signin`, or
[reset it](operator-credential.md).

**The provider says `redirect_uri_mismatch` (or "redirect URI is not valid").**
The URL registered in the provider's console is not byte-identical to the one
Umbral sends. Copy it again from the connector page
(`<origin>/connect/<slug>/callback`), check `http` vs `https`, the port, a
trailing slash and upper/lower case, and check that `UMBRAL__PUBLIC_ORIGIN` is
the address you actually use. If the Connectors screen shows an origin-mismatch
banner, fix the origin or the proxy first. Google can take a few minutes to
honour a new redirect URI.

**`This server only accepts HTTPS requests.`**
Umbral saw plain http. Either the proxy is not sending
`X-Forwarded-Proto: https` (see
[Using a different reverse proxy](https.md#using-a-different-reverse-proxy)), or you are
talking to port 8080 directly. The laptop trial is the only place plain http is
allowed.

**My app shows "That sign-in request was rejected".**
Umbral's own error page, with a reason. The usual two: the app's `redirect_uri`
is not exactly the one registered for it (compare character by character, as it
appears in the app's authorization request), or the app is not using PKCE with
`S256`. Libraries usually have a "use PKCE" switch.

**Someone clicks a provider and sees "Sign-in didn't complete".**
They see "Sign-in didn't complete" and nothing technical; the cause is in the security log.
Typical causes: a wrong or rotated client secret (open **Connectors**, use
**Replace client secret**), or a provider app that is not allowed to log that
person in (Google "Testing" mode: see the [Google walkthrough](../connectors/google.md)).
To read the log, query the database from the host (needs the `sqlite3` tool):

```bash
sqlite3 -readonly /path/to/umbral.db \
  "select occurred_utc, event_type, outcome, connector_slug, detail
     from security_events order by id desc limit 20;"
```

With a named Docker volume on Linux the path is usually
`/var/lib/docker/volumes/umbral-data/_data/umbral.db` (needs `sudo`); on Docker
Desktop, [copy the directory out](backup.md) and open the copy.

**A user may hold someone else's provider account.**
Before schema version 9, an address a person never proved could count as
verified, and the real owner of that address was then linked into that
person's user when they signed in through another provider (see the 0.8.0
notes in the [CHANGELOG](../../CHANGELOG.md)). The upgrade stops new joins of this kind but does
not undo old ones. This read-only query lists every user with more than one
provider account where at least one account does not, today, assert the user's
address as verified:

```bash
sqlite3 -readonly /path/to/umbral.db \
  "select u.id, u.display_name, u.email, la.provider_kind, la.asserted_email,
          la.asserted_email_verified, la.linked_utc
     from linked_accounts la join users u on u.id = la.user_id
    where la.user_id in (
            select l.user_id from linked_accounts l join users v on v.id = l.user_id
             group by l.user_id
            having count(*) > 1
               and coalesce(sum(l.asserted_email_verified = 1
                                and l.asserted_email = v.email collate nocase), 0) < count(*))
    order by u.id, la.linked_utc;"
```

A row is not proof of anything: a person may simply have changed their address
at one provider, or a provider may not have returned an address the last time
they signed in (GitHub, when its email lookup fails). Look at the order the accounts were linked in and at their
addresses. If a user holds the accounts of two different people, disable it on
**Users** at once. Deleting it gives each person a new user, with a new `sub`,
the next time they sign in, so apps see them as new people.

**After a restore or a lost volume, connectors or apps stop working.**
If `/data/keys` did not come back, stored secrets cannot be decrypted and tokens
no longer validate. In **Connectors** use **Replace client secret** for each
provider, and **Regenerate secret** for each app, then update the apps.
