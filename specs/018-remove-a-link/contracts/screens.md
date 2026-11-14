# Contract: removing a link on the Users screen

Ruled by `umbral-designer` on 2026-11-14 (answers to the five questions of the
proposal). Words are final unless a review changes them.

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
  Remove), behind a confirm (`Components.confirmSubmit`).
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
| Confirm (`{label}`, `{name}`) | Remove the {label} link from {name}? The person who signs in with that {label} account becomes a new user, and the email addresses it verified are no longer trusted here. This cannot be undone. |
| Notice `user-link-removed`, **Success** | Link removed. |
| Notice `user-link-last`, **Warning** (`{name}`) | That is {name}'s only way to sign in, so it was not removed. Delete the user, or invite them to add a passkey first. |
| Notice `user-link-not-found`, **Warning** | That link was already removed. |

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

## Captures for the review

At 1280 and 360, dark and light, in `.alta/reviews/2026-11-14-remove-a-link/`:

1. A card with two links and a passkey (the list shown).
2. A card with one link only (no list).
3. A card with two links of the same provider (same label, different dates).
4. The Success notice after a removal.
5. The two Warning notices.
