# Contract: The operator's surface

Every string is in `Views/Texts.fs`. Every POST is operator-only
(`AdminAuth.requireOperator`) and antiforgery-checked; the CSRF discovery
test covers them because they live under `/admin/`.

## The Passkey card on the Connectors screen

Drawn after the three provider cards and before the add form (so with no
connectors the order is Discord, GitHub, Google, Passkey). Not offered in the
add form. `<article data-connector="passkey" data-connector-kind="passkey"
data-connector-state="enabled|disabled" data-connector-shared="…">`.

Unconfigured (no row): heading **Passkey**, pill **Not configured**, lead
*Lets people sign in with their phone, laptop or security key, with no
provider account. Umbral is the provider.*, the form below with its defaults,
button **Enable passkeys**.

Configured: heading **Passkey**, the state and shared badges, the toggle
(`/admin/connectors/passkey/toggle`, the existing route) and delete
(`/admin/connectors/passkey/delete`, existing) buttons with the same
stranded-apps and last-enabled warnings as every connector. No store pill, no
callback URL, no client id, no secret, no allowed-domains field, no
naming-advice note.

The form, `POST /admin/connectors/passkey`:

- `shared` checkbox (**Shared with every app**, the existing hint).
- `enrolment` radio, `data-enrolment`: **Invitations only** (*Only people you
  send an invitation link to can create an account. The safe default.*) and
  **Anyone can sign up** (*Anyone who reaches a sign-in page that offers this
  connector can create an account.*). Default `invitations`.
- A line `data-rule="enrolment"`: *Who can sign in through this connector is
  decided by the enrolment mode above, not by the allowed email domains:
  passkeys carry no email.*
- Button **Save connector** (configured) / **Enable passkeys** (new).
- On save: creates the row (enabled, `created_utc` now) or updates `is_shared`
  and `passkey_enrolment`; unsharing warns about stranded apps exactly as the
  generic save does (`connector-strands-apps` with `change=unshare`, confirm
  with `?confirm=yes`); records `connector_created` or `connector_updated`
  with detail `enrolment=<mode>`; 302 `/admin/connectors?msg=passkey-connector-saved`
  (*Saved. People can sign in with a passkey from the next page load.*).
- Refusals: `enrolment` not one of the two → `connector-unknown`-style neutral
  notice `passkey-connector-invalid`, nothing saved.

The origin-mismatch banner the screen already shows is enough for FR-032's
"not hidden"; the card adds a hint `data-origin-bound`: *Passkeys are bound to
this address (`<origin>`). If it changes, every passkey stops working and
people need a new invitation.*

### Invitations on the card (configured only)

- A form `POST /admin/connectors/passkey/invitations` with `note` (**Note
  (optional)**, hint *For you: who the link is for. People never see it.*, up
  to 64) and button **Invite someone**. On success: the link is shown once on
  the redirected screen in a `data-invitation-link` copy field with the text
  *Send this link to the person over a channel you trust. It works once and
  for 7 days; it is not shown again.*, carried by a single-use Data-Protected
  cookie (research R9), notice `passkey-invitation-created`. Records
  `passkey_invitation_created` (`for=new`). A note over 64 chars → notice
  `passkey-invitation-invalid`, nothing created.
- The list **Open invitations**, one `<li data-invitation="<id>">` per open
  invitation: the note (or *No note*), **For**: *A new person* or the user's
  display name (`data-invitation-user="<user id>"`), **Created**, **Expires**,
  and a form `POST /admin/connectors/passkey/invitations/<id>/revoke` with
  button **Revoke** and `data-confirm` *Revoke this invitation? The link will
  stop working.* On success: notice `passkey-invitation-revoked`, event
  `passkey_invitation_revoked`. Revoking an invitation that is not open →
  notice `passkey-invitation-not-open`, nothing changes.
- Empty list: *No open invitations.*

## The Users screen

Each row (`<article data-user="…">`) gains:

- "Passkey" among the linked-provider pills when the user has one or more
  passkeys: `(Components.pill …).data("linked-provider", "passkey")`.
- A list `data-user-passkeys`, one `<li data-passkey="<id>">` per passkey:
  nickname, **Added** date, **Last used** date or *Never*, and a form
  `POST /admin/users/<user id>/passkeys/<id>/remove` with button **Remove**
  and `data-confirm` *Remove this passkey from <name>'s account? They will not
  be able to sign in with it.* On success: notice `passkey-removed`, event
  `passkey_removed` (`by=operator`, actor operator, user id). The operator may
  remove a user's last passkey (they can delete the user too); the user's
  session is untouched (removing a credential is not a sign-out).
- A form `POST /admin/users/<user id>/invite` with button **Invite to add a
  passkey**, shown while a passkey connector exists (enabled or not: the link
  can be used once it is enabled). On success: the link shown once on the
  Users screen (`data-invitation-link`, same cookie pattern, scoped to the
  user), notice `passkey-user-invited` (*Send this link to <name> over a
  channel you trust. It adds a passkey to their account, works once and for 7
  days, and is not shown again.*), event `passkey_invitation_created`
  (`for=user`, user id). With no passkey connector → notice
  `passkey-not-enabled`.

The row's htmx toggle fragment (`UserToggle`) re-renders the whole row, so it
draws the same list.

Deleting a user (existing route) cascades to `passkeys` and
`passkey_invitations`; the confirmation text already says the account is
removed for good.

## The Apps screen

The "unrestricted connectors" warning (spec 006 FR-010, `data-warning=
"unrestricted-connectors"`) counts the passkey connector as unrestricted when
its enrolment is **open**, restricted when **invitations only**. The
instance-list "Taking a list off" warning on the Connectors screen never
names the passkey connector.

## Security log, as the operator reads it

```sql
select occurred_utc, event_type, outcome, actor, user_id, connector_slug, detail
  from security_events where connector_slug = 'passkey' or event_type like 'passkey_%'
 order by id desc limit 50;
```

`sign_in_failed` rows carry `passkey_refused;reason=<word>;stage=<stage>`
(research R6). Nothing in the table or the log identifies a credential or an
invitation.
