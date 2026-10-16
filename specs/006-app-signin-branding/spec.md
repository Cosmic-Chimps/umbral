# Feature Specification: Per-App Sign-In Branding

**Feature Branch**: `006-app-signin-branding`

**Created**: 2026-10-15

**Status**: Implemented (Firefox and Safari browser checks of T033 pending)

**Input**: User description: "Per-app sign-in branding. An operator can give each App its own logo and accent colour, shown on the sign-in pages an end user sees when that App sends them to Umbral (the connector picker, and the error pages reached during that sign-in). Scope: per app only — no global branding screen and no new top-level admin screen (Principle I: Apps, Connectors, Users stay the three screens; branding is fields on the App edit page). When the request names no app, or the app has no branding, the current Umbral look is shown. Logo: uploaded only (no remote URL, so no third party learns the IP of everyone signing in); raster formats only (PNG, JPEG, WebP — no SVG, which can carry script); the operator crops it in the admin UI before saving; Umbral limits the file size and the pixel dimensions (width/height) and stores the image in the data directory (Principle II). The operator can replace or remove the logo. Accent colour: one colour (the primary/accent), entered as a strict #rrggbb value; the text colour on top of it is derived automatically for legible contrast; every other colour stays fixed. Admin pages are never branded. The branded page keeps a small "secured by Umbral" mark so a person can tell which sign-in page they are on. Documentation for the feature is part of done (Principle VI)."

## Background

Since feature 003 the sign-in page names the app a person is signing in to
("Sign in to Frunchy"). It still looks like Umbral, though, whatever app the
person came from. People who click "Sign in" in Frunchy, with its orange logo,
land on a page that looks unrelated to it. Some of them hesitate, and some
think they are being phished. An operator running five apps behind one Umbral
wants each app's sign-in page to look like part of that app.

Two decisions were taken before this spec was written:

- **Per app, not global.** Branding is part of an App, edited where the app is
  edited. There is no instance-wide branding screen. Such a screen would be a
  fourth top-level screen, which Principle I forbids.
- **Uploaded logos only.** A logo at a remote address would tell that address's
  owner the network address of everyone who signs in. Umbral only shows logos it
  serves itself.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An app's sign-in page carries its logo and colour (Priority: P1)

On the Apps screen, the operator opens Frunchy's branding and uploads
`frunchy.png`. They crop away the transparent margin around it and pick
Frunchy's orange, `#f26b1d`, as the accent colour, then save. The next person
who clicks "Sign in" in Frunchy lands on a sign-in page with the Frunchy logo
at the top and orange "Sign in with …" buttons, whose text is still easy to
read. A small "Secured by Umbral" mark at the bottom of the page shows which
service they are actually signing in through. Someone signing in to Recipes,
which has no branding, sees Umbral's usual page, unchanged.

**Why this priority**: This is the feature. A logo and a colour are enough for
an end user to recognise the app they came from.

**Independent Test**: Brand one of two registered apps, start a sign-in from
each, and check that only the branded app's sign-in page shows the logo and
accent colour. Check also that the branded page shows the "Secured by Umbral"
mark, that the unbranded page is unchanged, and that the admin screens look as
before.

**Acceptance Scenarios**:

1. **Given** an app with a logo and an accent colour, **When** an end user
   starts a sign-in from it, **Then** the sign-in page shows that logo and uses
   that accent colour for its sign-in buttons and highlighted elements.
2. **Given** that branded app, **When** its sign-in page renders, **Then** the
   text on accent-coloured elements is legible against the accent colour (see
   FR-016), and the page shows a "Secured by Umbral" mark.
3. **Given** a second app with no branding, **When** a sign-in starts from it,
   **Then** its sign-in page looks exactly as it did before this feature.
4. **Given** the sign-in page is opened without coming from an app, **When** it
   renders, **Then** it shows Umbral's own look.
5. **Given** a sign-in from the branded app fails at the provider (the person
   declines, or the provider is unreachable), **When** the person is sent back
   to the sign-in page with the failure message, **Then** the page is still
   branded for that app.
6. **Given** an operator signed in to the admin screens, **When** they browse
   Apps, Connectors or Users, **Then** no app's logo or accent colour changes the
   look of those screens. The app's own branding settings show the app's logo and
   colour as values.

---

### User Story 2 - Upload, crop and check a logo (Priority: P1)

The operator uploads a 3000 × 2000 photo of a logo printed on a mug. Umbral
shows the image and lets them drag a crop area around the logo itself. When
they save, Umbral keeps only the cropped area, scaled down to the stored size,
and shows the result on the app's branding settings at the size end users will
see it. Next to it is a sample button in the accent colour, so the operator can
check how the sign-in page will look before anyone signs in. Earlier the
operator had tried an SVG file and a 9 MB PNG. Umbral refused both, said why,
and said what to upload instead.

**Why this priority**: User Story 1 is not possible without a safe way to get a
logo into Umbral, so this story ships with it. It is listed separately because
the upload rules are where the security and memory risks are, and they are
tested on their own.

**Independent Test**: Upload one file of each accepted format and check that
each is accepted, can be cropped, and is stored no larger than the maximum
stored size. Then upload an SVG, an oversized file, a file whose content does
not match its name, and an image above the maximum pixel size, and check that
each is refused with a message saying what to do next.

**Acceptance Scenarios**:

1. **Given** a PNG, JPEG or WebP file within the limits, **When** the operator
   uploads it, **Then** Umbral shows the image with a crop area that starts as
   the whole image.
2. **Given** an uploaded image, **When** the operator adjusts the crop area and
   saves, **Then** the stored logo contains only the cropped area, scaled down
   if needed to fit the stored size limit, with its proportions unchanged.
3. **Given** an uploaded image, **When** the operator saves without adjusting
   the crop area, **Then** the whole image is used.
4. **Given** an SVG file, or a file in any format other than PNG, JPEG or WebP,
   **When** the operator uploads it, **Then** Umbral refuses it, names the
   accepted formats, and keeps the app's current logo, if it has one.
5. **Given** a file larger than the upload size limit, or an image larger than
   the maximum pixel dimensions, **When** the operator uploads it, **Then**
   Umbral refuses it, states the limit and the file's actual size or
   dimensions, and keeps the current logo.
6. **Given** a file named `logo.png` whose content is not a PNG, JPEG or WebP
   image, **When** the operator uploads it, **Then** Umbral refuses it as not an
   image it accepts.
7. **Given** a saved logo, **When** the operator views the app's branding
   settings, **Then** they see the logo at the size end users will see it, and a
   sample button in the accent colour with its derived text colour.

---

### User Story 3 - Change or remove branding (Priority: P2)

Frunchy gets a new logo. The operator uploads it, crops it and saves, and the
next person to sign in to Frunchy sees the new one rather than an old copy from
their browser's cache. Later the operator decides Recipes should look like
Umbral again. They remove Recipes' logo and clear its accent colour, and
Recipes' sign-in page goes back to Umbral's look.

**Why this priority**: Logos change. Without replace and remove, an operator who
makes a mistake has to delete and re-register the app, which changes its client
credentials.

**Independent Test**: Replace a branded app's logo and check that a returning
browser sees the new logo on its next sign-in. Then remove the logo and the
colour, and check that the sign-in page matches an unbranded app's.

**Acceptance Scenarios**:

1. **Given** an app with a logo, **When** the operator saves a new logo,
   **Then** the old logo stops being shown and is no longer kept, and the next
   sign-in page load shows the new logo, including for a browser that cached the
   old one.
2. **Given** an app with a logo, **When** the operator removes the logo, **Then**
   its sign-in page shows no app logo, and the accent colour, if set, still
   applies.
3. **Given** an app with an accent colour, **When** the operator clears it,
   **Then** its sign-in page uses Umbral's own accent colour, and the logo, if
   set, still shows.
4. **Given** a branded app, **When** the operator deletes the app, **Then** its
   logo is deleted with it.
5. **Given** a branding change, **When** it is saved or removed, **Then** the
   change is recorded in the security log with the app it applies to, as other
   app registration changes are.

---

### Edge Cases

- **The accent colour is hard to see against the page background.** A very dark
  accent on Umbral's dark page (`#0a0a10`) leaves accent-coloured outlines and
  highlights nearly invisible. Umbral warns the operator when they save such a
  colour, and the operator may save it anyway. The text on accent-coloured
  buttons stays legible whatever the colour (FR-016).
- **The accent colour is typed in another notation.** `f26b1d`, `#F26B1D`,
  `#fff`, `orange` and `rgb(242,107,29)`: only `#rrggbb` is accepted, letters
  in either case. Anything else is refused with an example of the expected
  form. The colour picker always produces the accepted form.
- **A crafted sign-in link names one app but returns to another.** Which app
  the branding comes from is decided exactly as for the app name on the page
  (feature 003): from the sign-in request the page will return to, never from a
  separate value. A link therefore cannot show app A's logo while signing in to
  app B.
- **A sign-in request names a client that is not registered.** The page shows
  Umbral's own look, as it does for an unknown app's name.
- **A protocol error Umbral cannot send back to the app**, such as an
  unregistered return address. The error page keeps Umbral's own look. The
  request failed validation, so it cannot be trusted to name the app, and an
  attacker must not be able to put a trusted app's logo on a page they built.
- **An animated image** (animated PNG or WebP). Only the first frame is kept.
- **An image with embedded metadata** (location, camera, author). The stored
  logo never contains it, because Umbral produces the stored logo from the
  decoded pixels and never stores the uploaded file itself.
- **A very small or very thin crop.** A crop smaller than the minimum logo size
  in either direction is refused, with the minimum stated.
- **Transparent images.** Transparency in PNG and WebP is kept. JPEG has none,
  so a JPEG logo is shown as a rectangle on the page background.
- **A browser where cropping is unavailable** (scripts disabled). The operator
  can still upload a PNG that already meets the stored logo limits (FR-013),
  and it is used whole. Cropping, scaling down and converting JPEG or WebP
  need scripts, and the form says so. Umbral refuses any other file with that
  explanation. (Changed at plan: research R1.)
- **Restoring a backup.** The logos are in the data directory, so copying the
  directory back restores every app's logo with the rest of its settings.
- **An upgrade from a version without this feature.** Every existing app has no
  branding, and every sign-in page looks as it did before.

## Requirements *(mandatory)*

### Functional Requirements

#### Where branding appears

- **FR-001**: Each App MUST be able to have an optional logo and an optional
  accent colour, set independently of each other. New and existing apps MUST
  have neither.
- **FR-002**: When a sign-in comes from a registered app that has branding, the
  sign-in page MUST show that app's logo (if set) and MUST use its accent colour
  (if set) in place of Umbral's accent colour. This includes the page shown
  again with a failure message during that sign-in.
- **FR-003**: Which app's branding applies MUST be decided in the same way, and
  from the same request, as which app the sign-in page names (feature 003,
  FR-001). No other value on the page's address can select an app's branding.
- **FR-004**: The sign-in page MUST show Umbral's own look when the sign-in comes
  from no app, from an unregistered client, or from an app with no branding.
- **FR-005**: The protocol error page for errors that cannot be returned to the
  app, the end-session pages and every admin screen MUST NOT be branded.
- **FR-006**: Every sign-in page MUST show that it is served by Umbral. An
  unbranded page does this with its existing Umbral header and footer. A
  branded page does it with a "Secured by Umbral" mark. App branding MUST NOT
  be able to hide, recolour or replace it.
- **FR-007**: The app's logo MUST be shown with the app's name as its text
  alternative. The page MUST still name the app in text, as it does today.
- **FR-008**: Branding MUST change only the logo and the accent colour. Every
  other colour, the layout, the wording and the fonts MUST stay the same.

#### Logo upload

- **FR-009**: The operator MUST be able to set an app's logo only by uploading
  an image file from their own device. Umbral MUST NOT accept an address of an
  image hosted elsewhere, and MUST NOT make the sign-in page load anything from
  another origin.
- **FR-010**: Umbral MUST accept only PNG, JPEG and WebP images, judged by the
  file's content and not by its name or declared type. Any other file, including
  SVG, MUST be refused.
- **FR-011**: Umbral MUST refuse an uploaded file larger than 2 MB, and an image
  wider or taller than 4096 pixels, before processing it.
- **FR-012**: The operator MUST be able to crop the uploaded image to any
  rectangle within it before saving. With no crop chosen, the whole image MUST
  be used. A crop smaller than 32 pixels in either direction MUST be refused.
- **FR-013**: Umbral MUST produce the stored logo itself from the cropped
  pixels, scaled down if needed so that it fits within 512 × 512 pixels with its
  proportions unchanged. Umbral MUST NOT store or serve the uploaded file's
  bytes as they were received, and the stored logo MUST NOT contain the uploaded
  file's metadata.
- **FR-014**: Every refused upload MUST leave the app's current logo unchanged,
  and the message MUST say what was wrong and what to upload instead, naming the
  accepted formats or the limit exceeded and the file's actual value.

#### Accent colour

- **FR-015**: The accent colour MUST be entered as `#rrggbb` (six hexadecimal
  digits after `#`, letters in either case). Any other form MUST be refused,
  with an example of the expected form. The operator MUST be able to choose the
  colour with a colour picker as well as by typing it.
- **FR-016**: Umbral MUST choose the colour of text and icons placed on the
  accent colour itself. Of the dark and light text colours it can use, it MUST
  choose the one with the higher contrast against the accent colour, so that
  contrast is never below 4.5:1.
- **FR-017**: When the accent colour has a contrast below 3:1 against the
  sign-in page background, Umbral MUST warn the operator on saving, saying that
  outlines and highlights in that colour will be hard to see. The operator MAY
  save it anyway.

#### Managing branding

- **FR-018**: The operator MUST set, replace and remove an app's logo and accent
  colour from the Apps screen, in that app's settings. This feature MUST NOT add
  an admin screen or a top-level concept beyond App, Connector and User.
- **FR-019**: The app's settings MUST show its current logo at the size it
  appears on the sign-in page, and a sample button in the accent colour with its
  derived text colour.
- **FR-020**: After a logo is replaced, the next load of the sign-in page MUST
  show the new logo, even in a browser that cached the previous one. A replaced
  or removed logo MUST be deleted from storage.
- **FR-021**: Deleting an app MUST delete its logo.
- **FR-022**: Every change to an app's branding (logo set, replaced or removed;
  accent colour set, changed or cleared) MUST be recorded in the structured
  security log, naming the app, as other app registration changes are.
- **FR-023**: Changing branding MUST follow the same rules as other admin
  changes: only a signed-in operator can make it, and it MUST carry the same
  protection against forged requests.

#### Storage and resources

- **FR-024**: Logos MUST be stored in Umbral's data directory, so that copying
  the directory backs them up and copying it back restores them.
- **FR-025**: Logos MUST be served by Umbral to anyone, without signing in,
  because the sign-in page is shown before anyone is signed in. Umbral MUST
  serve them only as images, in a way that a browser cannot treat as a page or a
  script.
- **FR-026**: Processing an upload at the maximum limits MUST NOT stop Umbral
  running within the memory of a Raspberry Pi-class machine, and MUST NOT raise
  Umbral's idle memory use above the 150 MB target.

#### Documentation

- **FR-027**: The README, where Apps are documented, MUST describe how to brand an
  app: the accepted formats and limits, cropping, the accent colour and its
  contrast warning, where branding appears and where it does not, and the
  "Secured by Umbral" mark.

### Key Entities

- **App** (changed): gains an optional logo and an optional accent colour.
  Neither is a separate concept: each exists only as part of an app, is edited
  with the app, and is deleted with it.
- **Logo** (part of an App): the image produced by Umbral from the operator's
  cropped upload, at most 512 × 512 pixels, in the data directory. It has a
  public address that changes whenever the logo is replaced.
- **Accent colour** (part of an App): one `#rrggbb` value, from which Umbral
  derives the text colour used on it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In every sign-in started from a branded app, the sign-in page
  shows that app's logo and accent colour. In every other sign-in, the page is
  identical to the page before this feature.
- **SC-002**: An operator brands an existing app with a logo they already have
  as a file, crop included, in under 2 minutes, without reading the
  documentation first.
- **SC-003**: For every possible accent colour, the text on accent-coloured
  elements of the sign-in page has a contrast of at least 4.5:1.
- **SC-004**: Of a test set containing SVG files, files with mislabelled content,
  files over 2 MB and images over 4096 pixels, 100% are refused with a message
  saying what to upload instead, and none changes the app's current logo.
- **SC-005**: Opening a sign-in page, branded or not, makes no request to any
  origin other than Umbral's own.
- **SC-006**: After an upgrade, no existing app's sign-in page changes until the
  operator brands it.
- **SC-007**: Umbral's idle memory use stays under 150 MB, and the time from
  starting the container to a first sign-in stays within the five-minute target.

## Assumptions

### Decisions taken without an explicit answer

Each of these has an alternative a reasonable operator could prefer. They are
marked for confirmation at clarify, and each is cheap to reverse now.

| Requirement | Assumed answer | Why this one | Alternative |
| --- | --- | --- | --- |
| FR-011, FR-013 | Upload at most 2 MB and 4096 px on either side. Stored logo fits within 512 × 512 px | 2 MB covers any real logo export. 4096 px bounds the memory one decode can take on a Pi. 512 px is sharp on high-density screens at the size the page shows | Higher limits, which accept phone photos straight from the camera but cost more memory per upload |
| FR-012 | Free crop, any proportions | Logos range from square marks to wide wordmarks, and a fixed shape would force one of them into empty margins | A fixed square crop, simpler to lay out but bad for wordmarks |
| FR-005 | The protocol error page that cannot return to the app stays unbranded | That request failed validation. Branding it would let anyone put a trusted app's logo on an error page they built | Brand it whenever the request names a registered client |
| FR-017 | A low-contrast accent colour gets a warning, not a refusal | The operator knows their brand colour. Text on the colour is always legible, so the remaining risk is only faint highlights | Refuse colours below 3:1 against the background |
| FR-006 | The "Secured by Umbral" mark is always shown and cannot be configured | It is how an end user tells Umbral's real page from a lookalike. If branding could remove it, it would prove nothing | Let the operator hide it |

### Context assumptions

- The sign-in page already knows the app a sign-in comes from (feature 003), so
  this feature does not change how an app is identified. It only adds what is
  shown for it.
- An operator brands a handful of apps, so the logos take a negligible part of
  the data directory.
- Dark mode is the only theme Umbral has. Branding defines one accent colour and
  no light-mode variant.
- Out of scope: a global or instance-wide branding, custom fonts, custom
  wording, background images, favicons per app, custom CSS, and branding of the
  admin screens. Each would be either a fourth screen or a theme editor, which
  Principles I and VII rule out.
