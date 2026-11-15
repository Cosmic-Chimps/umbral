# Documentation review: screenshots in the docs (task v6pm)

Branch `alta/buoyant-taiga-cxt3`, `ea5d6ae..881d768` on main 4958e12. Reviewed 2026-11-15 by the
documentation reviewer: every picture in its light version (and `app-created` in its dark), against
its alt text and the text around it, and the built quick start in `site/quick-start-dark.png`.

## Verdict

**Clear, no blocking items, four nits.** The pictures are the right ones in the right places, each
alt says what its picture shows, and the site build carries them without asking another host for
anything. None of the nits needs a re-check; take what you agree with.

## What was checked, and the rulings you asked for

- **Placement.** The README's sign-in page comes after the opening paragraph: the product, seen
  before the details. The GitHub pane comes inside quick start step 4 item 1, right where the text
  says to copy the redirect URI. The app pane comes after "Click **Create app**", where the reader
  looks for the client ID and secret. In branding.md the branded page sits under the paragraph about
  "Secured by Umbral". In passkey.md and fediverse.md each step's picture sits under the text that
  describes it. In operator-passkeys.md the Credential page sits under the description of its three
  parts. None is in the way of the text. The one exception is look.md (N1).
- **The ten alt texts.** Each names the screen, then what is on it, in the product's own words
  (**Sign in to Larder**, **Redirect URI to register with the provider**, **App registered**,
  **Where is your account?**, **Continue with your passkey**, **Turn password sign-in off**). I
  compared each with its picture: all accurate. The longest (the branded page, about 200 characters)
  is still one sentence a screen reader reads without trouble. Not too long.
- **look.md's new sentence**, "An app can show its own logo and colour. Its sign-in page then carries
  them in place of Umbral's gold and name bar", is true to branding.md and introduces its picture.
  Right.
- **Your passkeys without "Your passkey is ready."** Accepted. The picture shows the page, not the
  moment. Publishing the notice drawn as an error (`njtn`) would be worse. Retake it once `njtn` is
  fixed.
- **Step 4 as a loose list.** Accepted. The built page shows the items spaced a little more, and the
  picture belongs to item 1. That is better than a picture outside the list that reads as step 5.
- **site/README.md**: "The screenshots" says what runs, what it needs, what is made up, why three
  files change on every run, and how to add a screen. The two "What fails the build" entries match
  `collect.mjs` and `check-dist.mjs`. Right, apart from N3.
- **No changelog.d note**: agreed. Nothing changes for whoever runs Umbral.

## Nits

1. **look.md's first picture follows the operator screens.** The paragraph above it ends on the
   operator screens' side rail, and the picture is the sign-in page. Either move the picture before
   "The operator screens have…", so it follows "Umbral has one look, called Umbra…", or start the
   next line "The sign-in page people see:". As it stands, the reader looks for a side rail in the
   picture.
2. **The README's opening will get a second item after the first paragraph.** The demo task (fzzx,
   step 10) adds "**Try it:** <https://umbral-demo.lacalabaza.xyz>…" "after the first paragraph",
   which is where this picture now sits. Decide the order now: the line, then the picture, keeps the
   link in the first screenful. I will tell the demo session too.
3. **"commit the files of the screens you changed, and put the others back with `git checkout
   docs/images/`".** Read in the other order, that command throws away the new captures too. Write
   it as two steps: "Commit the files of the screens you changed first, then `git checkout
   docs/images/` puts the others back."
4. **The site's Theme select and the pictures.** The known limit is stated: a reader who picks Light
   on a dark system sees dark pictures on a light page. That is fine for now. If it is fixed later,
   site/README's sentence goes with it.

Outside this review, for `umbral-designer`: on the passkey step the button **Continue with your
passkey** carries the link icon, while the picker's **Sign in with a passkey** carries the key icon.
The pictures make that visible.
