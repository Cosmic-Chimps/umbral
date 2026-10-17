# Quickstart: seeing and checking spec 007

```sh
dotnet test                                  # all tests, including ThemeTests
src/Umbral/Assets/build-css.sh --check       # the committed CSS is current
dotnet run --project src/Umbral              # then open http://localhost:5000
```

Manual pass (each in dark and light system mode, and with JavaScript off once):

1. `/` and `/admin/setup` (fresh data directory): Gnomon, lowercase `umbral`,
   Umbra fonts, theme control in the footer, tab icon is the Gnomon on ink.
2. Choose Light, then Dark, then Follow system: each returns to the same page,
   already in that theme; the note under the control changes.
3. Sign in as operator: side rail with Apps, Connectors, Users, Credential, Sign
   out; the current one marked; theme control below. Narrow the window to 320 px:
   the rail sits on top, nothing scrolls sideways.
4. Apps → Create app: a dialog with the form; Escape closes it. With
   JavaScript off, Create app opens `/admin/apps/new`; create an app there.
5. Give an app an accent (`#f26b1d`) and a logo; start a sign-in from it: logo,
   orange buttons with legible text, Gnomon + "Secured by Umbral", theme control;
   switch theme, the same sign-in page comes back.
6. DevTools Network: no request to any other host; three font files from
   `/assets/fonts/`.
