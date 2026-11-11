# Documentation review: spec 012, several redirect URIs per app (task r3du)

Branch: `alta/primal-zanzibar-dfrt`, the docs commit `cf47ee2` in `d6d2373..3650f8f`.
Reviewed 2026-11-11 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, the spec and `contracts/routes.md`, and the code
(`Domains/RedirectUris.fs`, `Handlers/Admin/AppRedirectUris.fs`, `Views/Texts.fs`,
`Infrastructure/SecurityEvents.fs`).

## Verdict

**Clear to merge.** No blocking item. Three nits, none needs a re-check.

## What was checked

- **`docs/integrate.md`.** "Matched exactly … not one of them, character for character"; the
  limits paragraph: up to 10 of each (`RedirectUris.maxPerApp = 10`, refused with
  `app-redirect-uri-limit`), the first given at creation and the rest added on the app's page, the
  client ID and secret unchanged, at least one redirect URI kept (`RedirectUris.LastOne`, refused
  with `app-redirect-uri-last`, whose message is the doc's "add the new one first"), wildcards
  refused, every addition and removal logged (`app_updated` with details `redirect_uri_added`,
  `redirect_uri_removed`, `post_logout_redirect_uri_added`, `post_logout_redirect_uri_removed`).
  The Signing out paragraph's "one of the post-logout URIs … Umbral redirects only to those" is
  OpenIddict's check against the registered list, as before. The new closing paragraph
  ("The client ID is generated, and the app's name and type are chosen when you create it") is the
  part of the old limits paragraph that is still true.
- **`docs/native-apps.md`.** "Several redirect URIs (up to 10, added on its page)", matched exactly
  each; the post-logout sentence; the Desktop tip of two or three fixed ports, which works because
  each is a separate exact registration.
- **UI words.** "Redirect URIs", "Post-logout redirect URIs", "Add a redirect URI" and the refusal
  messages are in `Texts.fs` as `contracts/routes.md` gives them. The docs quote only the
  established labels (**Apps**, **Sign-in methods**) and describe the rest, which is right.
- **The README.** Nothing in it needs to change: no line says one redirect URI or "delete the app
  to change it". The quick start's "It is matched **exactly**; wildcards are refused" stays true.
- **The rest of the doc set.** I grepped `README.md`, `docs/` and every sample README for wording
  that assumes one redirect URI per app. The only old-rule phrasing left is in the Next.js sample
  (nit 2). The samples' "What to create in Umbral" sections give one redirect URI each, which is
  what a sample needs.
- **The changelog fragment.** `### Added` only, and no Upgrade note is right: `SchemaVersion` is 9
  before and after, and OpenIddict already stores the lists. `changelog.sh check` passes.
- **Links.** `check-links.py`: 286 links in 35 files, all resolve.

## Nits

1. **`docs/integrate.md`, "Every addition and removal is written to the security log."** Name it,
   as the passkey and allowed-domains pages name theirs, so an operator can query for it: "as an
   `app_updated` event whose detail is `redirect_uri_added`, `redirect_uri_removed`,
   `post_logout_redirect_uri_added` or `post_logout_redirect_uri_removed`." The changelog's
   sentence can stay as it is.
2. **`samples/nextjs/README.md:72`**, "Umbral refuses the sign-in because that redirect URI is not
   the one registered." Now "is not one of those registered". One word, true before and after.
3. **`docs/native-apps.md:81`** is one long unwrapped line after the edit ("matches each
   **exactly**, character for character: … and"). Rewrap to the file's width. And a heads-up for
   whoever merges second: task spgx (loopback redirect on any port, in `crisp-lark-n2hx`) rewrites
   the Desktop apps section, so this branch's two-or-three-ports tip will be superseded; the merge
   should keep spgx's text and drop the tip if spgx makes any port work.

## notes.md

The docs commit is small and each statement maps to the spec and the code above.

## Answers (implementer, 2026-11-11)

1. Taken. `docs/integrate.md` now says every addition and removal is an `app_updated` event in the
   security log, with the URI and one of the four details.
2. Taken. `samples/nextjs/README.md:72` now says "not one of those registered".
3. Taken. The paragraph is rewrapped to the file's width. On the merge: noted in the spec
   (research R3) and in the merge notes. Keep spgx's Desktop apps text, and drop the
   two-or-three-ports tip.
