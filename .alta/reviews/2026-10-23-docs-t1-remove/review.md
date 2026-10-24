# Review: T1, remove what no longer exists, and fix the contradictions

Task: `.alta/tasks/2026-10-23-docs-t1-remove-what-no-longer-exists-t1rm.md`,
audit `docs/audit-2026-10.md` T1 (W5, W6, W7, W13, W15, W16, W17, W18, W23,
W25) plus the removal of the `continue-on-error` bridge from T8.
Branch: `alta/festive-marble-n7qj`, commit `15aacab` on `main` at `1f3a89e`.
Reviewed 2026-10-23 by the documentation reviewer session, from the diff, the
implementer's `notes.md`, the code each statement cites, and my own runs.

## Verdict

**Clear to merge.** No blocking item. Three nits, none needs a re-check.

## What was checked

Each finding against the diff, and each new statement against the code:

- **W15, W16, W17 (oidc.md).** The configuration-file subsection is gone whole,
  and "The issuer and the identifier cannot be changed" now follows "If the
  provider cannot be reached" with nothing orphaned between them. The Extra
  scopes row is gone; the step-3 Scopes row now says the three scopes are the
  only ones and there is no setting for more, which `Domains/Connectors.fs:139`
  and the add handler confirm. The new Allowed email domains row says what
  `ConnectorDomainsHelp` says (`Texts.fs:944-946`) with the placeholder from
  `:936`, and sits where the field sits in the form (after Shared with every
  app, `Handlers/Admin/Connectors.fs:544-599`). The reserved list matches
  `parseKind` (`Domains/Connectors.fs:57-65`): discord, github, google, fake,
  oidc, passkey.
- **W5, W6 (screens).** One wording in README:166-167, README:415-417 and
  PRODUCT.md:14 and :103; "four screens" and "four operator" appear nowhere.
  The rail has those four entries and Sign out (`Views/Layout.fs:52-55`).
  Catching PRODUCT.md:14, which the audit missed, was right.
- **W7 (schema).** "schema version" occurs once in the README, in the new
  `### Upgrading` at the end of "Your data: back it up". The quoted refusal
  ("…written by a newer Umbral. Run the newer image, or restore the backup of
  the data directory taken before the upgrade.") is verbatim from
  `Database.fs:386-389`. The branding section keeps its two true sentences and
  links to `#upgrading`, which resolves.
- **W13, W18, W23, W25.** `specs/`, one folder per feature; "easiest provider
  to register an app with" (right: a passkey needs no app anywhere, so a bare
  "easiest" would not hold); "most demanding provider to set up"; "(since
  0.3.0)" matches CHANGELOG.md; the compose comment no longer claims nothing
  is published.
- **The link check.** On the branch: exit 0, "155 links in 12 files, all
  resolve". The `continue-on-error` line and its three comment lines are gone
  from the `links` job and nothing else in `ci.yml` changed. The failing-run
  proof in `notes.md` (the W15 anchor appended to a throwaway copy, exit 1)
  is the right shape; I did not repeat it, since I ran the same proof for
  T8's review yesterday on the same script.
- **The audit's acceptance grep.** Seven hits, all in `docs/audit-2026-10.md`
  quoting the findings it reports; none in a walkthrough or the README. The
  criterion was written before the audit itself landed under `docs/`, so the
  hits are the audit's own text and the criterion is met. Leaving the audit
  untouched was right.
- **No CHANGELOG entry.** Agreed: every removed statement described something
  that does not exist, and no statement people rely on changed meaning.
- **No CI run.** Noted; the branch is local and the job is one `run:` of a
  script that passes here.

## Nits

1. **oidc.md:49, "`fake`, the test connector".** Correct, since `parseKind`
   reserves it, but no operator will ever meet a connector called fake, and the
   phrase invites the question "what test connector?". Either drop `fake` from
   the parenthesis (an operator who types it gets the same refusal as for any
   reserved name) or say "and `fake`, which Umbral's own tests use".
2. **README:531, the heading "Where it is kept, and upgrading".** The upgrading
   text moved out, so the heading now promises more than the section holds.
   "Where it is kept" would do; the paragraph still links to Upgrading.
3. **README:1128, "nothing changes until you use what the upgrade added".**
   Good sentence, but the two examples that follow are the only two upgrades
   so far, and the next one (spec 010's per-instance clients) will not fit
   "every app starts with no branding, and nobody can use a passkey". Worth
   phrasing the examples as "for example" so the sentence survives the next
   schema step.

## notes.md

Complete under section 6 of the audit: files, each finding and its change in
a table, every behavioural statement with the code it was checked against,
the acceptance greps with their output, the link check before and after, the
bridge removal and the failing-run proof, and what was not done and why.
Agreed on all of it.
