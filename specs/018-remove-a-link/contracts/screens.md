# Contract: removing a link on the Users screen

Status: **proposal, for `umbral-designer`**. The questions are at the end. Words
and layout become final once answered; a review may still change them.

## Where

The user's card (`Handlers/Admin/Users.fs`, `row`). Today it has the provider
pills, the passkey list (each passkey: nickname, added, last used, a red ghost
**Remove** with a confirm), the invite button, the sign-in dates, and the
Disable and Delete buttons.

## Proposal

- **The pills stay as they are.**
- **A list of the linked accounts**, drawn the way the passkey list is: one row
  per linked provider account with its label (the pill's), "Linked:
  yyyy-mm-dd", "Last used: yyyy-mm-dd", and a red ghost small button **Remove
  link** (`Components.btnSm Ghost` + `text-error`, like the passkey Remove; One
  Red Fill: Delete keeps the only fill). Placed above the passkey list.
- **Drawn only when at least one link can be removed**: the user has two or
  more ways in (links plus passkeys). A user with one link and no passkey, most
  users, sees no change on the card. In the list, a link that is the last way
  in cannot occur (two ways in means each link has another beside it).
- The dates are what lets the operator tell the wrong link from the right one
  (the stranger's was linked later, and two accounts of one provider carry the
  same label). They are data Umbral already holds (`linked_accounts.linked_utc`,
  `last_used_utc`), and stay within "the linked providers, first and last
  sign-in" of the screen's minimal contract (spec 001, admin-and-config.md).

| Part | Words |
| --- | --- |
| Row dates | Linked: 2026-11-02 · Last used: 2026-11-13 (labels `PasskeyAddedLabel`-style: "Linked", "Last used") |
| Button | Remove link |
| Confirm (`data-confirm`, `{label}`, `{name}`) | Remove the {label} link from {name}? Whoever signs in with that {label} account becomes a new user, and its email addresses are no longer trusted here. This can't be undone. |
| Notice `user-link-removed` (Success) | Link removed. |
| Notice `user-link-last` | That is the user's only way to sign in, so it stays. Delete the user instead, or invite them to add a passkey first. |
| Notice `user-link-not-found` | That link was already removed. Reload the page to see the user's current links. |

## Test hooks

| Hook | On |
| --- | --- |
| `data-user-links` | The list |
| `data-user-link="{fingerprint}"` | A row |
| `data-user-link-remove` | The row's button |

## Questions

1. The list, drawn only when a link can be removed, above the passkeys: yes, or
   another place (for example a remove control on each pill)?
2. The dates on each row: yes?
3. Button "Remove link", red ghost small: yes?
4. The confirm sentence and the three notices.
5. The two refusal notices are shown in the existing notice alert: Success tone
   for the removal; which tone for "last" and "not found" (today's
   `user-not-found` is drawn in the same Success alert, since the `view`
   function has one tone)?
