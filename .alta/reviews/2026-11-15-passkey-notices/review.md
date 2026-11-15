# Design review: the notices on Your passkeys

Task njtn. Branch `alta/glad-gentian-hpdg`, `84edf46`. Reviewed 2026-11-15 by the design
reviewer from two of the sixteen captures (`notice-added-360-light`, `notice-last-1280-dark`)
and the implementer's description of the change.

## Verdict

**Clear to merge. No blocking items; one nit.**

- "Your passkey is ready." is a Success alert with the check icon; the description says
  "Passkey removed." is the same. That is the fix.
- A refusal still reads as one, under the heading, with the next action in its sentence.

## Nit

1. **Two of the four refusals are rules, not failures.** On the operator screens a refusal
   that follows a rule is a Warning alert and red is kept for something that went wrong. Here
   that would be: `passkey-last-cannot-remove` and `passkey-limit-reached` as Warning;
   `passkey-registration-failed` stays Failure; `passkey-not-found` as Warning, like the
   "already removed" notices elsewhere. Take it here if the helper makes it one line each;
   otherwise leave it and it goes to the queue.

## Outside this task

The rows on this page predate two later decisions: a row's Remove is a ghost in the error
colour with a confirm (One Red Fill rule), and a passkey's dates read "Added 2026-11-15 ·
Never used". Passed to the queue as its own small task.

No re-check needed.
