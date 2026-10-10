---
title: "Hide the operator nav on end-user and unauthenticated pages"
kind: problem
status: pending
created: 2026-10-10
summary: "Layout.page renders Apps/Connectors/Users/Sign out for every page, including the end-user picker (/signin) and every message page, so an end user sees the operator's navigation."
---

## Why
`Layout.page` (src/Umbral/Views/Layout.fs, the `navLinks ctx t` call in the header) renders the operator nav (Apps, Connectors, Users, a sign-out form) unconditionally. Its own doc comment says unauthenticated screens "simply don't link into it", but Setup, SignIn, the end-user picker `/signin` and every message page all go through `page`, so an end user mid-login sees links to `/admin/*` and a sign-out button.

## What to do
Give `page` a way to know whether the viewer is the operator (e.g. a parameter or the `Admin` path/auth state) and render the nav only then; the picker, setup, sign-in and end-user message pages show the brand only. Add a test that GET /signin has no `/admin/` links.

## Where
src/Umbral/Views/Layout.fs (`navLinks`, `page`); callers in Handlers/Connect/Picker.fs, Handlers/Admin/Setup.fs, SignIn.fs, and `messagePageWith`.
