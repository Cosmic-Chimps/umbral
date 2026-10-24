module Umbral.Tests.PasskeyInvitationTests

open System
open Xunit
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure

let private seed (app: Fixtures.TestApp) =
    task {
        do!
            ConnectorStore.save
                app.DbConfig
                app.Clock
                { Slug = "passkey"
                  Kind = Passkey
                  DisplayName = "Passkey"
                  ClientId = ""
                  ClientSecretRef = ""
                  ClientSecretHint = ""
                  IssuerUrl = None
                  AllowedEmailDomains = []
                  PasskeyEnrolment = InvitationsOnly
                  FediverseAccess = FediverseAccess.Any
                  IsEnabled = true
                  IsShared = true
                  CreatedUtc = app.Clock.GetUtcNow()
                  UpdatedUtc = app.Clock.GetUtcNow() }
                None
    }

let private user (app: Fixtures.TestApp) (id: string) =
    Users.insertForTesting
        app.DbConfig
        app.Clock
        { Id = id
          DisplayName = $"User {id}"
          Email = None
          EmailVerified = false
          AvatarUrl = None
          PreferredUsername = None
          ProfileUrl = None
          IsEnabled = true
          FirstSeenUtc = app.Clock.GetUtcNow()
          LastSeenUtc = app.Clock.GetUtcNow() }

let private stateOf (app: Fixtures.TestApp) (secret: string) =
    task {
        let! found = PasskeyInvitations.findBySecret app.DbConfig app.Clock secret
        return found |> Option.map snd
    }

[<Fact>]
let ``a created invitation is open and listed`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! invitation, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None "note"
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Open, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Equal<string list>([ invitation.Id ], open' |> List.map _.Id)
    }

[<Fact>]
let ``consuming twice succeeds once`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! invitation, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None ""

        let consume () =
            task {
                use! connection = Database.openConnection app.DbConfig
                use transaction = connection.BeginTransaction()
                let! spent = PasskeyInvitations.consumeIn connection transaction app.Clock invitation.Id
                do! transaction.CommitAsync()
                return spent
            }

        let! first = consume ()
        let! second = consume ()
        Assert.True first
        Assert.False second
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Used, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Empty open'
    }

[<Fact>]
let ``an invitation expires after seven days`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! _, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None ""
        app.Clock.Advance(TimeSpan.FromDays 7. + TimeSpan.FromSeconds 1.)
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Expired, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Empty open'
    }

[<Fact>]
let ``a revoked invitation is revoked`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! invitation, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" None ""
        let! revoked = PasskeyInvitations.revoke app.DbConfig app.Clock invitation.Id
        Assert.True revoked
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.Revoked, state)
    }

[<Fact>]
let ``an invitation for a disabled user is user-gone`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        do! user app "u1"
        let! _, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" (Some "u1") ""
        do! Users.setEnabled app.DbConfig "u1" false
        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.UserGone, state)
        let! open' = PasskeyInvitations.listOpen app.DbConfig app.Clock "passkey"
        Assert.Empty open'
    }

[<Fact>]
let ``an invitation for a deleted user is user-gone`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        do! user app "u2"
        let! _, secret = PasskeyInvitations.create app.DbConfig app.Clock "passkey" (Some "u2") ""

        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- "PRAGMA foreign_keys = OFF; DELETE FROM users WHERE id = 'u2'"
        let! _ = command.ExecuteNonQueryAsync()

        let! state = stateOf app secret
        Assert.Equal(Some PasskeyInvitations.UserGone, state)
    }

[<Fact>]
let ``an unknown secret finds nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        do! seed app
        let! found = PasskeyInvitations.findBySecret app.DbConfig app.Clock "nope"
        Assert.True found.IsNone
    }

// ---------------------------------------------------------------------------
// Recovery: an invitation for an existing user (spec 008 US5, T025)
// ---------------------------------------------------------------------------

module private Recovery =
    open System.Net
    open System.Text.RegularExpressions
    open Umbral.Tests.Infrastructure.Journeys
    open Umbral.Tests.Infrastructure.Flows
    open Umbral.Tests.Infrastructure.PasskeyFlows

    let authenticator () =
        SoftwareAuthenticator.SoftwareAuthenticator("umbral.test", Fixtures.TestOrigin)

    let prepareRecovery (app: Fixtures.TestApp) =
        task {
            let operator = Browser(app.Client)
            do! claimOperator app operator
            do! enablePasskeyConnector operator "invitations"
            let! clientId, secret = registerApp operator "Grafana"
            return operator, clientId, secret
        }

    /// Ana, enrolled through a new-person invitation; her id and authenticator.
    let enrolAna (app: Fixtures.TestApp) (operator: Browser) =
        task {
            let! link = inviteNewPerson operator "Ana"
            let first = authenticator ()
            let! r, _ = registerThroughInvitation (Browser(app.Client)) first (secretOf link) (Some "Ana") "Phone"
            Assert.Equal(HttpStatusCode.OK, r.StatusCode)
            let! id = scalar app "SELECT id FROM users WHERE display_name = 'Ana'" []
            return id, first
        }

    /// Invite the user from the Users screen; returns the revealed link's secret.
    let inviteUser (operator: Browser) (id: string) =
        task {
            let! response = operator.PostForm("/admin/users", $"/admin/users/{id}/invite", [])
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Equal($"/admin/users?msg=passkey-user-invited&user={id}", location response)
            let! html = operator.GetHtml(location response)
            let m = Regex.Match(html, "<input[^>]*data-invitation-link[^>]*>")
            Assert.True(m.Success)
            let v = Regex.Match(m.Value, "value=\"([^\"]+)\"")
            return html, secretOf (string (WebUtility.HtmlDecode v.Groups[1].Value))
        }

open Recovery
open System.Net
open System.Text.RegularExpressions
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows
open Umbral.Tests.Infrastructure.PasskeyFlows

[<Fact>]
let ``the operator invites an existing user; the link is shown once on that row`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepareRecovery app
        let! anaId, _ = enrolAna app operator

        let! html, _ = inviteUser operator anaId
        let article = Regex.Match(html, "<article[^>]*data-user=\"" + anaId + "\".*?</article>", RegexOptions.Singleline)
        Assert.True article.Success
        Assert.Equal(1, Regex.Matches(html, "data-invitation-link").Count)
        Assert.Contains("data-invitation-link", article.Value)
        Assert.Contains("Ana", Regex.Match(html, "data-notice=\"passkey-user-invited\".*?</div>", RegexOptions.Singleline).Value)

        let! again = operator.GetHtml $"/admin/users?msg=passkey-user-invited&user={anaId}"
        Assert.DoesNotContain("data-invitation-link", again)

        let! card = operator.GetHtml "/admin/connectors/passkey"
        Assert.Contains($"data-invitation-user=\"{anaId}\"", card)

        let! created = events app "passkey_invitation_created"
        Assert.Equal<(string option * string option * string option) list>(
            [ Some anaId, Some "passkey", Some "for=user" ],
            created |> List.filter (fun (u, _, _) -> u = Some anaId)
        )
    }

[<Fact>]
let ``the invitation page for an existing user names the account and asks for no name`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepareRecovery app
        let! anaId, _ = enrolAna app operator
        let! _, secret = inviteUser operator anaId
        let! page, _ = openInvitation (Browser(app.Client)) secret
        Assert.Contains("data-screen=\"invitation\"", page)
        Assert.Matches("data-invitation-account[^>]*>[^<]*Ana", page)
        Assert.DoesNotContain("name=\"displayName\"", page)
    }

[<Fact>]
let ``registering through it adds a second passkey to the same person, who can then sign in`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, clientId, clientSecret = prepareRecovery app
        let! anaId, _ = enrolAna app operator
        let! _, secret = inviteUser operator anaId

        let second = authenticator ()
        let person = Browser(app.Client)
        let! response, body = registerThroughInvitation person second secret None "Laptop"
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        Assert.Equal("/account/passkeys?msg=passkey-added", strOf body "redirect")

        let! passkeys = scalar app "SELECT count(*) FROM passkeys WHERE user_id = @u" [ "@u", anaId ]
        Assert.Equal("2", passkeys)
        let! users = scalar app "SELECT count(*) FROM users" []
        Assert.Equal("1", users)
        let! used = scalar app "SELECT count(*) FROM passkey_invitations WHERE used_utc IS NOT NULL" []
        Assert.Equal("2", used)

        let! registered = events app "passkey_registered"
        Assert.Equal<(string option * string option * string option) list>(
            [ Some anaId, Some "passkey", Some "via=invitation"; Some anaId, Some "passkey", Some "via=invitation" ],
            registered
        )

        let! authorized = person.Get(authorizeUrl clientId "openid")
        Assert.Contains("code=", location authorized)

        let browser = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn browser clientId "openid"
        let! signIn, signInBody = signInWithPasskey browser second (List.head second.Credentials).Id returnUrl token
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode)
        let! code = codeFromRedirect browser (strOf signInBody "redirect")
        let! exchanged = exchangeCode app clientId clientSecret code
        let! tokens = json exchanged
        Assert.Equal(anaId, strOf (segmentOf 1 (strOf tokens "id_token")) "sub")
    }

[<Fact>]
let ``an invitation for a disabled or deleted user cannot be used`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let! operator, _, _ = prepareRecovery app
        let! anaId, _ = enrolAna app operator

        let reason (secret: string) =
            task {
                let! html, _ = openInvitation (Browser(app.Client)) secret
                Assert.Contains("data-screen=\"invitation-unusable\"", html)
                let! failed = events app "sign_in_failed"
                let _, _, detail = List.last failed
                return detail
            }

        let! _, first = inviteUser operator anaId
        let! toggled = operator.PostForm("/admin/users", $"/admin/users/{anaId}/toggle", [])
        Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode)
        let! detail = reason first
        Assert.Equal(Some "passkey_refused;reason=invitation_user_gone;stage=invitation", detail)

        let! _, second = inviteUser operator anaId
        let! deleted = operator.PostForm("/admin/users", $"/admin/users/{anaId}/delete", [])
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode)
        let! left = scalar app "SELECT count(*) FROM passkey_invitations WHERE user_id = @u" [ "@u", anaId ]
        Assert.Equal("0", left)
        let! detail2 = reason second
        Assert.Equal(Some "passkey_refused;reason=invitation_unknown;stage=invitation", detail2)
    }

[<Fact>]
let ``a user who signed in only through another provider can add a passkey and keeps one identity`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        do! seedFakeConnector app
        let! operator, clientId, clientSecret = prepareRecovery app

        let fake = Browser(app.Client)
        let! viaFake = signInViaFake fake clientId "openid" "fake-subject-1" "bo@example.com"
        let fakeCode = match viaFake with | Ok c -> c | Error e -> failwith e
        let! exchanged = exchangeCode app clientId clientSecret fakeCode
        let! tokens = json exchanged
        let fakeSub = strOf (segmentOf 1 (strOf tokens "id_token")) "sub"
        let! boId = userIdOf app "fake-subject-1"
        Assert.Equal(boId, fakeSub)

        let! _, secret = inviteUser operator boId
        let authn = authenticator ()
        let! r, _ = registerThroughInvitation (Browser(app.Client)) authn secret None "Phone"
        Assert.Equal(HttpStatusCode.OK, r.StatusCode)

        let browser = Browser(app.Client)
        let! returnUrl, _, token = startPasskeySignIn browser clientId "openid"
        let! _, body = signInWithPasskey browser authn (List.head authn.Credentials).Id returnUrl token
        let! code = codeFromRedirect browser (strOf body "redirect")
        let! exchanged2 = exchangeCode app clientId clientSecret code
        let! tokens2 = json exchanged2
        Assert.Equal(boId, strOf (segmentOf 1 (strOf tokens2 "id_token")) "sub")

        let again = Browser(app.Client)
        let! viaFake2 = signInViaFake again clientId "openid" "fake-subject-1" "bo@example.com"
        Assert.True(Result.isOk viaFake2)
    }

[<Fact>]
let ``without a passkey connector the row offers no invitation and a direct post is refused`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        do! seedFakeConnector app
        let operator = Browser(app.Client)
        do! claimOperator app operator
        let! clientId, _ = registerApp operator "Grafana"
        let! _ = signInViaFake (Browser(app.Client)) clientId "openid" "fake-subject-2" "cy@example.com"
        let! id = userIdOf app "fake-subject-2"

        let! html = operator.GetHtml "/admin/users"
        Assert.Contains($"data-user=\"{id}\"", html)
        Assert.DoesNotContain("data-user-invite", html)

        let! response = operator.PostForm("/admin/users", $"/admin/users/{id}/invite", [])
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("passkey-not-enabled", location response)
    }
