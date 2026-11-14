# Contract: removing a link on the Users screen

Ruled by `umbral-designer` on 2026-11-14 (answers to the five questions of the
proposal, and a second answer on the confirmation once passkeys went with the
link). Words are final unless a review changes them.

## Where

The user's card (`Handlers/Admin/Users.fs`, `row`). Today it has the provider
pills, the passkey list (each passkey: nickname, added, last used, a red ghost
**Remove** with a confirm), the invite button, the sign-in dates, and the
Disable and Delete buttons.

## The list of linked accounts

- **The pills stay as they are.** No control inside a pill: a pill is a label.
- **A list above the passkey list**, drawn the way the passkey list is, under a
  field-style label **Linked accounts** (`um-label`). The passkey list gets the
  matching label **Passkeys** when it is drawn, so the two lists on one card are
  told apart.
- One row per linked provider account: its label (the pill's), the dates, and a
  small ghost button **Remove link** in the error colour with its hover class
  (`Components.btnSm Ghost` + `text-error hover:text-error`, like the passkey
  Remove). It carries **no** `data-confirm`: it asks for the confirm block
  below.
- **Drawn only when a link can be removed**: the user has two or more ways in
  (links plus passkeys). A user with one link and no passkey sees no change.
- The dates are data Umbral already holds (`linked_accounts.linked_utc`,
  `last_used_utc`) and tell the wrong link from the right one.
- DESIGN.md's One Red Fill rule gains the sentence (this task): "A destructive
  action on one row of a list is a ghost in the error colour with a confirm,
  whether or not it can be undone; the red fill is the card's own deletion."

| Part | Words |
| --- | --- |
| List label | Linked accounts |
| Passkey list label | Passkeys |
| Row dates | Linked 2026-11-02 · Last used 2026-11-13 (or "Never used") |
| Button | Remove link |
| Notice `user-link-removed`, **Success** | Link removed. |
| Notice `user-link-last`, **Warning** (`{name}`) | That is {name}'s only way to sign in, so it was not removed. Delete the user, or invite them to add a passkey first. |
| Notice `user-link-not-found`, **Warning** | That link was already removed. |

## The confirm block

"Remove link" posts without `confirm`; nothing is removed. The server redraws
the Users page at 422 (same search) with that card's row expanded into a
confirm block in place of the row's button, the product's pattern (Remove
passkey on the Credential screen, Turn trust off on a connector). Focus goes
to the block. It is the confirmation with and without script.

| Part | Words |
| --- | --- |
| Question (`{label}`, `{name}`) | Remove the {label} link from {name}? The person who signs in with that {label} account becomes a new user, and the email addresses it verified are no longer trusted here. |
| When n ≥ 1, a list under | These passkeys were added while signed in with it, and are removed too: |
| When passkeys of unknown origin were added after the link, a second list under (`{date}` the link's date) | These passkeys were added since {date} and may be that person's. They stay. Remove any you do not recognise: |
| Each line of both lists | {nickname}, added {date} |
| Last line | This cannot be undone. |
| Buttons | small Danger **Remove link** (posts `confirm=remove`); ghost **Cancel**, a link back to `/admin/users` with the same search |

A link that is gone by the time of the confirm post gets `user-link-not-found`;
a confirm post that would take the last way in gets `user-link-last`.

## The passkey Remove

Removing a passkey now also signs out what was signed in with it. Its confirm
text (`UserPasskeyRemoveConfirm`) gains "and signs out anything signed in with
it"; nothing else changes.

## Notice tones

The Users view gets a `noticeKind` function, as Apps and Connectors have: the
two refusals are **Warning** alerts, and the existing `user-not-found` moves to
Warning with them. Everything else stays Success.

`user-link-last` names the user, so it is redirected with the user's id
(`WebForms.redirectWithNoticeAbout ... [ "user", id ]`, as `passkey-user-invited`
is) and the view looks the name up among the rows; when the user is not among
them (a search), the sentence says "the user" in place of the name.

## Test hooks

| Hook | On |
| --- | --- |
| `data-user-links` | The list |
| `data-user-link="{fingerprint}"` | A row |
| `data-user-link-remove` | The row's button |
| `data-user-link-confirm` | The confirm block |
| `data-user-link-gone-passkeys` | Its list of passkeys removed too |
| `data-user-link-check-passkeys` | Its list of passkeys to check |

## Captures for the review

At 1280 and 360, dark and light, in `.alta/reviews/2026-11-14-remove-a-link/`:

1. A card with two links and a passkey (the list shown).
2. A card with one link only (no list).
3. A card with two links of the same provider (same label, different dates).
4. The Success notice after a removal.
5. The two Warning notices.
6. The confirm block with no passkeys, with a removed-too list, and with a
   check-these list.
7. Focus in the block, at 1280 dark only.
