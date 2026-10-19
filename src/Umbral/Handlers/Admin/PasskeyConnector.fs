module Umbral.Handlers.Admin.PasskeyConnector

open Umbral.Views

/// Placeholder until this module's routes land (spec 008 T014): it reads the
/// texts they will show, so the "every Texts field is read" check passes in the
/// meantime. The task that writes the page replaces it with the page.
let private _texts (t: Texts.Texts) : objnull list =
    [ box t.PasskeyCardLead
      box t.PasskeyEnableButton
      box t.PasskeyEnrolmentInvitationsLabel
      box t.PasskeyEnrolmentInvitationsHint
      box t.PasskeyEnrolmentOpenLabel
      box t.PasskeyEnrolmentOpenHint
      box t.PasskeyEnrolmentRule
      box t.PasskeyOriginBound
      box t.PasskeyInviteNoteLabel
      box t.PasskeyInviteNoteHint
      box t.PasskeyInviteButton
      box t.PasskeyInvitationLinkNote
      box t.PasskeyOpenInvitationsHeading
      box t.PasskeyNoOpenInvitations
      box t.PasskeyInvitationNoNote
      box t.PasskeyInvitationForLabel
      box t.PasskeyInvitationForNew
      box t.PasskeyInvitationCreatedLabel
      box t.PasskeyInvitationExpiresLabel
      box t.PasskeyInvitationRevokeButton
      box t.PasskeyInvitationRevokeConfirm ]
