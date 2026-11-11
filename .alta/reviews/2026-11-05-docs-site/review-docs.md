# Documentation review: the documentation site and the README split (task hmfr)

Task: `.alta/tasks/2026-11-05-docs-a-documentation-site-starlight-on-github-pages-and-the-hmfr.md`
(supersedes the audit's T7). Branch: `alta/roaming-foxglove-m2q5`, commits `1c19227` (the
split), `ca96f84` (the site and the workflow) and `65a2f69` (notes and captures), on `main` at
`3c87b72`. Reviewed 2026-11-11 by the documentation reviewer session, which also wrote the task,
per section 6 of `docs/audit-2026-10.md`, from the diff, `notes.md`, my own clean build and my own
check that nothing was lost. `review.md` in this folder is the designer's; this file is mine.

## Verdict

**Clear to merge, with one condition outside this branch.** One blocking item, which belongs to
the follow-up task c36q and only needs writing into it; four nits. Your six decisions are
ruled on below: five accepted as they are, one accepted with a nit. One re-check, for the
blocking item's routing and whichever nits you take.

## What was checked

- **The build, from clean.** With Node 22.23.4: `rm -rf node_modules dist .astro`, `npm ci`,
  `npm run build`. 29 pages (28 and the 404), Pagefind index built, "All internal links are
  valid", and `check-dist`: "48 files read (29 html, 6 css, 13 js), nothing requested from another
  host; 148 external `<a>` links". `git status` afterwards shows nothing new outside `site/` (only
  the designer's uncommitted `review.md`), so the collect script writes only where it should.
- **Nothing lost: my own check, not yours.** A separate script took every sentence of
  `README.md` at `3c87b72`, outside code fences, normalised link targets, heading marks, bullets and
  wrapping, and looked for it in the new `README.md` and `docs/**/*.md` (without the audit and
  `docs/history/`). 39 sentences are not found verbatim, and they are exactly your list: the old
  Contents block (34), the passkey paragraphs (766–817), the two quick-start cross-references
  (902), "the compose file above" (978), "the section above" (1014) and "Details are in the
  walkthroughs" (1393). Every other sentence moved word for word.
- **The passkey paragraphs not kept twice.** I checked each fact of README 766–817 against
  `docs/connectors/passkey.md`, not just the section names: the two enrolment modes, the 7-day
  single-use link and "treat it like a password", **Revoke**, **Continue with your passkey**,
  `/account/passkeys` and the limit of 10, **Create a recovery link** with its hint and
  **Recovery link** / **Invitation link**, removal from **Users** "when a device is stolen", the
  claims an app receives and "should not offer the passkey connector", the address binding and
  "a new invitation per person", and JavaScript with "operator actions work without it". All are
  there. Nothing a reader needs was dropped.
- **The new sentences** (your list under "New sentences"): the Contents block's pointer to the
  site, the Integrate closing paragraph of seven links (each anchor resolves), the one-paragraph
  "Run it for real" and "Your data: back it up" (each made only of what the moved sections say),
  the Contributing sentence on `site/`, the lead of `docs/integrate.md`, the two-bullet opening of
  `docs/passkeys.md`, and the opening of `docs/run/secret-stores.md` (true to the settings table).
  None states anything new about behaviour.
- **The README.** 412 lines (under 700). The quick start is the text on `main` but for seven link
  targets, as the task wanted; T3 still owns it.
- **Acceptance, the rest.** No frontmatter in any `docs/**/*.md` (the build enforces it); `git diff
  3c87b72 -- src tests Dockerfile` is empty; `site` is in `.dockerignore`; `check-links.py`: 270
  links in 34 files, all resolve; the changelog fragment is Added and Changed in the prescribed
  format and `changelog.sh check` passes; the audit gains exactly one line under T7.
- **Not checked by me:** the captures and the look (the designer's), and the deployed site, which
  does not exist yet. Your notes say the same and give the maintainer the steps.

## Blocking (for the follow-up c36q, not this branch)

1. **A log message still sends operators to the README's Troubleshooting.** `src/Umbral/Program.fs:556`,
   the schema-9 upgrade warning: "To look for accounts already joined this way, see \"A user may
   hold someone else's provider account\" under Troubleshooting in the README." After this merge
   that entry is in `docs/run/troubleshooting.md`, and the README has no Troubleshooting section.
   An operator reading that warning searches the README and finds nothing: a Principle VI
   failure, on a security message. This branch rightly does not touch `src/` (the task forbids
   it), and c36q, which repoints the strings that cite documentation, does not list it. **Add it to
   c36q's task file** as one more item: the message names the site's page,
   `https://cosmic-chimps.github.io/umbral/run/troubleshooting/`, by its `DocsUrl`. Say in
   `notes.md` that it is there; that is the re-check.

## Nits

1. **`.github/workflows/release.yml:40`**, a comment: "Setup (Bella Baxter, variables, the
   production environment): README, \"Deploying on Caravana\"". Now `docs/run/caravana.md`. Not in
   `src/`, so it can change here; one line.
2. **Decision 1, the page behind "Passkeys for the admin area".** `docs/passkeys.md` is titled
   "Passkeys" and opens with both uses, the people's and the operator's, then the security-log and
   memory paragraphs about people's passkeys, then "Passkeys for the admin area". The sidebar row
   and the README's Contents both call it "Passkeys for the admin area", so a reader who clicks it
   lands on a page with another title, and one looking for the passkey connector's log events under
   **Connectors → Passkeys** does not find the "nothing is ever logged" sentence there. The task
   said existing pages change only in links; as its author I lift that for these two paragraphs.
   Move the security-log paragraph (its last sentence is the one the walkthrough lacks) and the
   memory paragraph into `docs/connectors/passkey.md` (Gotchas has the events already), and make
   `docs/passkeys.md` the admin-area page alone, ideally `docs/run/operator-passkeys.md`, titled as
   its sidebar row. If you would rather not move text now, rename the row to the page's title
   instead; either way, one name for one page.
3. **The Contents block's site links are dead until the first deploy** (your maintainer's step 4).
   Fine as a sequence, but say it in the PR description too, so the maintainer merges and runs the
   `pages` workflow in one sitting.
4. **The README's "Run it for real" paragraph**, "The whole of it, why `UMBRAL__PUBLIC_ORIGIN` must
   be exactly the address people use, and what another reverse proxy must send are in …" reads
   awkwardly. "The rest is in [Run it for real, behind HTTPS](docs/run/https.md): why
   `UMBRAL__PUBLIC_ORIGIN` must be exactly the address people use, and what another reverse proxy
   must send." says the same. Optional.

## The six decisions

1. **Two passkey pages and an extra sidebar row.** Accepted, with nit 2.
2. **`/integrate/` as one page from two sources.** Accepted. The three values and the snippet
   stay written once, in the README, and the site reads as one page. Dropping the GitHub-only lead
   on the site is right.
3. **Requirements on the start page.** Accepted.
4. **The Contents block after the opening.** Accepted, and the better place: my task's list was an
   inventory, not an order.
5. **"Secret stores" as a nested group, its first row the overview.** Accepted.
6. **README slicing with a closed list that fails on an unknown section.** Accepted. That failure
   is what makes slicing safe: a new `##` in the README cannot vanish from the site in silence.

## notes.md

Complete under section 6 and more: the README line ranges to their new homes, the loss check and
its exceptions, the passkey table, every new sentence, the six decisions put as questions, the
acceptance item by item with what was not checked said plainly, the T4 map for when it runs, and
the maintainer's steps. My own check found the same exceptions and nothing else.
