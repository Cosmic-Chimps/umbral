# Contract: the trust setting on the OpenID Connect screens

Ruled by `umbral-designer` on 2026-11-13 (answers to Q1–Q4). Words are final
unless a review changes them.

## The group (add form and pane)

A `fieldset` of two `.um-choice` radio cards, side by side from `md` (like the
Fediverse scope and passkey enrolment groups), stacked below. Field name
`emailTrust`, values `on` and `off`. Placed **before the Allowed email domains
field** in both forms. The amber `ConnectorEmailTrustWarning` alert on the add
form is removed: its substance is the group's lead.

| Part | Words |
| --- | --- |
| Legend | Verified email addresses from this provider |
| Lead (hint text under the legend) | Some providers mark an address as verified without checking it. Where anyone can sign up, a person could claim someone else's address. |
| Card `on`, title | Trusted |
| Card `on`, body | An address this provider marks as verified counts as verified here. People with the same address at another provider are the same account, and allowed email domains apply. For a provider you run or trust. |
| Card `off`, title | Not trusted |
| Card `off`, body | Every person from this provider is a separate account, whatever address it reports, and allowed email domains can't apply. For a provider where anyone can sign up, such as a public forge. |
| No choice (422, under the group) | Choose whether to trust this provider's verified email addresses. |
| Pane only, hint under the group | A change applies from the next sign-in. Turning trust off unlinks nobody who is already linked. Addresses that only this provider verified become unverified. |

- **Add form**: neither card checked on a first visit; a refused form keeps the
  operator's choice (or none).
- **Pane**: the stored value checked (a NULL row: `off`).

## Refusals and warnings with allowed email domains

| Case | What the operator sees |
| --- | --- |
| The connector's **own** list with `off` (add form or pane) | Refused, 422, every typed value kept but the secret, under the domains field: "Allowed email domains need verified addresses, and this provider's are not trusted. Trust them, or clear this list." |
| `off`, no own list, the **instance** list set | Three ways, like "offered nowhere": (1) the list entry carries the attention marker in place of the state badge; (2) a Warning alert under the pane heading: "Nobody can sign in through this provider. The email domains allowed for every connector need a verified address, and this provider's are not trusted. Trust them, or clear that list."; (3) the Set-up rule line says the same in one sentence. |
| `off`, no list anywhere | The Set-up rule line: "Who can sign in: anyone with an account at this provider. Each is a separate account, because its addresses are not trusted." |

The rule line for (3) of the instance case: "Nobody can sign in through this
provider: the email domains allowed for every connector need a verified
address, and its addresses are not trusted." (implementer's sentence; the
design review rules).

## Confirming trust off (pane)

A save of an existing connector that turns trust from on (or NULL) to off is
not applied at once. The pane is redrawn (422) with the typed values (the secret
never) and, under the pane heading, a confirm block in the pattern of the
unshare confirmation (`strandWarning`):

- a line: "Turning trust off makes {n} stored email addresses unverified: only
  this provider verified them. Nobody is unlinked, and turning trust on again
  doesn't verify them again." For `n = 1`: "1 stored email address", "verified it",
  "doesn't verify it again". For `n = 0`: "Turning trust off makes no stored
  email address unverified. Nobody is unlinked." (implementer's sentences; the
  design review rules);
- a Danger button "Turn trust off", a form posting the typed values (no secret)
  with `emailTrust=off` to the pane's save with `?confirm=trust-off`;
- Cancel: a link back to the pane.

`n` is computed by the same statements as the real save, rolled back.
A typed new secret is not carried by the confirm form; the pane's secret field
then shows the "type it again" hint.

## Log

As data-model.md: `connector_created` with `email_trust=on|off`;
`connector_updated` with `email_trust=on` or `email_trust=off;lowered=<n>` when
the setting changed.

## Captures (design review)

In `.alta/reviews/2026-11-13-email-trust/`, `<state>-<width>-<theme>.png` at 1280
and 360, dark and light:

1. `add-nochoice` and `add-refused` (no choice, 422);
2. `add-on`, `add-off` (each card chosen);
3. `pane` (existing connector, trust on);
4. `pane-ownlist-refused`;
5. `list-instance-off` (the list entry with the marker), `pane-instance-off` (alert and rule line);
6. `pane-off-nolist`;
7. `pane-confirm-off` (the confirm block).