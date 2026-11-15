# Code review, task v6pm (docs screenshots)

Reviewer: the `umbral` session. Range `4958e12..881d768` on `alta/buoyant-taiga-cxt3`: the capture
script under `site/scripts/screenshots/`, and `ea5d6ae` (`collect.mjs`, `check-dist.mjs`,
`check-links.py`, `site/.gitignore`, `umbra.css`).

**Verdict: clear to merge from the code side, with two nits.** Nothing blocking.

## What holds

- **No fake connector.**
  - The Compose environment is the quick start's three settings and nothing else, and the comment
    says why.
  - The walk stops if the **Provider** list offers `fake`.
  - The signed-in person comes from a passkey invitation and a CDP virtual authenticator.
- **Teardown on every exit.**
  - `down -v --remove-orphans` runs before the run and in the `EXIT` trap, which also runs on
    `set -e` failures.
  - `/data` is the image's anonymous volume, which `down -v` removes.
  - The Compose project is named after the checkout's path, so two worktrees don't tear each other
    down (the hdcm rule).
  - `in-container.sh` gives `out/` back to the host user, so a Linux run can delete it.
- **The secrets.**
  - The operator password comes from `reset-credential` through `exec -T`. It is never echoed,
    and Umbral's logs, which `run.sh` prints on failure, don't contain it.
  - The app's secret and the invitation link are never read into a variable that is logged.
    `describe()` prints `innerText`, which doesn't include input values, so the revealed secret's
    `<input>` doesn't show, and it blanks every run of 24+ token characters in the text and the
    URL.
  - The failure screenshot is skipped on `app-created`.
  - `out/` is git-ignored, and the instance is destroyed at the end.
  - The secret visible in `app-created-*.png` belongs to an instance that no longer exists, as the
    task allows.
- **Themes:**
  - `emulateMedia` with no cookie and no change to the page;
  - a check that the two themes really differ (the body background);
  - the theme control truthfully reads "Following your system".
- **Frames:**
  - `clipOf` takes the union of the selectors' boxes, padded and clamped to the page.
  - `fitWidth` picks the smallest viewport at which the named fields show whole.
  - `settle` waits for network idle, fonts and images, so the cuts are deterministic. 14 of 16
    files came out byte-identical across two runs, and the other two differ only by the random
    client ID and secret.
- **`collect.mjs`:**
  - Only images under `docs/images/` are copied, and only the ones a page references.
  - An image a page *shows* from anywhere else stops the build, while a plain *link* to one still
    goes to GitHub.
  - `rel` comes from `path.normalize` relative to the repository, so `../` can't escape
    `docs/images/`.
  - `public/images/` is cleared before the copy, and only that folder.
  - A `srcset` with a descriptor or a comma is refused with a message.
- **The two known gaps in the line-based scan both fail closed.**
  - An alt text that wraps onto a second line, and a reference-style image (`![a][ref]`), are read
    as links.
  - An image under `docs/images/` is still rewritten to the site's copy and copied: `imageHref`
    runs before the `embedded` test.
  - An image from anywhere else becomes a GitHub URL in an `<img>`. `check-dist` refuses that
    (another host), and so would the site's CSP.
  - So neither gap can publish a broken image or one loaded from another host. It's fine to leave
    them documented.
- **`check-dist.mjs`, `checkOwnImage`:**
  - It resolves an `<img src>` and each `srcset` URL of `<source>` and `<img>` against the page's
    own path under the base.
  - It requires the path to be under `BASE_PATH`, and the file to exist in `dist/`.
  - A missing image fails the build. That answers the question in your note.
  - `data:` and other hosts are left to the existing checks.
- **`check-links.py`:** `srcset` added to `HTML_ATTR`. A value with a descriptor reads as a
  missing file, so it fails, which matches `collect.mjs`. Run here: 454 links in 40 files, all
  resolve. `docs/images/` is 528 KB in 16 files.
- **Pins:** Playwright 1.64.1 is the same as `samples/test`, and the lock file is committed.

## Nits

**N1. The failure screenshot's guard is by step title, not by what the page shows.** If the step
after `app-created` failed before `op` navigated away, `_failure.png` would hold the revealed
secret. Guard on the content instead: skip the screenshot when
`await current.page.locator('[data-secret-once]').count() > 0`. It's harmless today (the file is in
`out/`, which is git-ignored, and the instance is gone), but the content check is the rule that
can't drift when steps are reordered.

**N2. SVG in `IMAGE_FILE`.** An SVG copied to the site is served from the docs' own origin, and
opened directly it runs any script it holds. Today only maintainer-authored files under
`docs/images/` can get there, and the eight screenshots are PNG. Either drop `svg` from
`IMAGE_FILE` until one is needed, or keep it and add a line to `site/README.md`: "an SVG in
docs/images/ must hold no script".

## Re-check

None needed for the nits. Take them or leave them as noted.
