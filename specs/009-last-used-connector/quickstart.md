# Quickstart: see "Last used" working

1. Run Umbral locally with two connectors enabled (for example the fake
   connector in development and the passkey connector), and register an app.
2. Start a sign-in from the app. The picker shows the buttons in their usual
   order, no badge.
3. Sign in through the second button. Open the browser's cookies for Umbral:
   `umbral_last_connector` holds that connector's slug and nothing else.
4. Sign out of the app and start a sign-in again. That button is first with a
   **Last used** badge, then "or", then the other button.
5. Disable that connector on the Connectors screen and reload: the page is as
   in step 2.
6. Give the app an accent colour and switch the theme between Light and Dark:
   the badge stays readable on the accent in both.
