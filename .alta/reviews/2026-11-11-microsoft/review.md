# Design review: the Microsoft accounts connector (spec 016)

Task m5ft. Branch `alta/warm-monsoon-8gdd`, captures from a build of `07b350e` (`050666b`),
the new rule for a failed check at `0511a79`. Reviewed 2026-11-13 by the design reviewer from
three of the 64 captures (`pane-not-set-up-1280-dark`, `refused-several-360-light`,
`connectors-list-set-up-1280-light`), `captures.md` and the contract's notices section. The
two notices already being fixed (findings 1 and 2) were not reviewed as found. Microsoft's own
pages were not read by the reviewer.

## Verdict

**Clear to merge once the fix already under way lands. No blocking items; two nits.**

## What was checked

- **List**: Microsoft after Apple while not set up, with no marker.
- **Set-up**: the naming note with its source, the redirect URI with copy, the secret-expiry
  caveat in the agreed words.
- **Settings**: display name with its hint, the client ID in mono, the secret, then "Who may
  sign in" as four stacked cards with **none chosen** on a new connector, each ending with
  Microsoft's label for the registration; the tenant field under the group, always in the
  page, with its hint; the shared box and allowed domains.
- **Refusals** at 422 under the field that is wrong, several at once, values kept, the secret
  box empty; the group's refusal tied to all four radios.
- **Once saved**: the pills, the masked secret with its last characters and the replace field,
  the closing row with one red fill.
- **Picker**: the display name on an ordinary button with the link icon, taking the accent.

## The new rule for a failed check (`0511a79`)

Right in substance and in place: a changed choice that Microsoft does not confirm is not
saved, with the message under the group or under the tenant field; an unchanged choice keeps
the connector as it was with a notice that says so. "Saved. Umbral could not check the
connector with Microsoft (<reason>), so it stays enabled as it was." reads right. One wording
point is nit 1.

## Your questions

- (3) The tenant field empty after saving another card: fine. Only one choice has a tenant.
- (4) A GUID scrolling inside its input at 360: acceptable. It is an editable field, and its
  whole value is one tap away; nothing is lost.
- (5) Microsoft and Fediverse sharing the link icon: as the provider-marks rule has it.
- (6) Microsoft moving up once saved: yes, that is the list's rule for every kind (connectors
  set up first, then the kinds not set up).

## Nits

1. **"check the tenant ID" under the group.** The first failed-check message ends "check the
   tenant ID, then save again", but it is also shown under the group when the choice is not
   "one organization", where there is no tenant ID. Give it two endings: under the tenant
   field, "Nothing was saved: check the tenant ID, then try again."; under the group, "Nothing
   was saved: check that the choice matches the app's registration, then try again." ("try
   again" in both, as the other panes now say.)
2. **"Who can sign in: anyone with an account" beside "Who may sign in".** The Set-up rule line
   is about email domains; on this pane it sits above a choice that also limits who signs in,
   and with one organization chosen "anyone with an account" is not true. On the Microsoft
   pane, lead the line with the saved choice, for example "Who can sign in: work or school
   accounts from one organization, with any email domain." If that reaches beyond this task,
   say so and I will pass it to the queue.

## Re-check

None needed for the nits beyond `pane-set-up` at 1280 in one theme if nit 2 is taken.
