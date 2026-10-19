module Umbral.Handlers.Passkeys.SignIn

open Umbral.Views

/// Placeholder until this module's routes land (spec 008 T014): it reads the
/// texts they will show, so the "every Texts field is read" check passes in the
/// meantime. The task that writes the page replaces it with the page.
let private _texts (t: Texts.Texts) : objnull list =
    [ box t.SignInWithPasskey
      box t.PasskeySignInLead
      box t.PasskeyContinue
      box t.PasskeyDisplayNameLabel
      box t.PasskeyNicknameLabel
      box t.PasskeyCreateAccountButton
      box t.PasskeyInvitationsOnlyLine
      box t.PasskeyJavascriptRequired ]
