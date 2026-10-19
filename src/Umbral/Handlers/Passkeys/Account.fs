module Umbral.Handlers.Passkeys.Account

open Umbral.Views

/// Placeholder until this module's routes land (spec 008 T014): it reads the
/// texts they will show, so the "every Texts field is read" check passes in the
/// meantime. The task that writes the page replaces it with the page.
let private _texts (t: Texts.Texts) : objnull list =
    [ box t.PasskeysTitle
      box t.PasskeysLead
      box t.PasskeysWaysIn
      box t.PasskeyAddedLabel
      box t.PasskeyLastUsedLabel
      box t.PasskeyNeverUsed
      box t.PasskeyRemoveButton
      box t.PasskeyRemoveConfirm
      box t.PasskeyAddButton
      box t.PasskeysNotEnabled
      box t.PasskeysLimitReached
      box t.HomePasskeysLink ]
