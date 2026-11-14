/// Removing one linked provider account on the Users screen (specs/018, T003):
/// the list, the confirm block, the removal and its refusals, and what is
/// recorded.
module Umbral.Tests.UserLinkAdminTests

open System
open System.Net
open System.Text.RegularExpressions
open Xunit
open Umbral.Domains
open Umbral.Domains.Connectors
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows

let private run (app: Fixtures.TestApp) (sql: string) =
    task {
        use! connection = Database.openConnection app.DbConfig
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! _ = command.ExecuteNonQueryAsync()
        return ()
    }

let private count (app: Fixtures.TestApp) (sql: string) =
    task {
        let! value = scalar app sql []
        return int value
    }

let private addUser (app: Fixtures.TestApp) (id: string) (name: string) =
    run
        app
        $"INSERT INTO users (id, display_name, email, email_verified, first_seen_utc, last_seen_utc)
          VALUES ('{id}', '{name}', '{id}@example.com', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')"

let private addLink (app: Fixtures.TestApp) (userId: string) (kind: string) (subject: string) (linked: string) (used: string) =
    run
        app
        $"INSERT INTO linked_accounts (provider_kind, provider_subject, user_id, asserted_email, asserted_email_verified, linked_utc, last_used_utc)
          VALUES ('{kind}', '{subject}', '{userId}', '{userId}@example.com', 0, '{linked}', '{used}')"

/// A passkey named after its id, created at `created`, with the given origin
/// (None: NULL).
let private addPasskey (app: Fixtures.TestApp) (userId: string) (id: string) (created: string) (via: string option) =
    task {
        do!
            run
                app
                $"INSERT INTO passkeys (id, user_id, credential_id, public_key, nickname, created_utc)
                  VALUES ('{id}', '{userId}', x'{Convert.ToHexString(Text.Encoding.UTF8.GetBytes id)}', x'AA', 'Key {id}', '{created}')"

        match via with
        | Some v -> do! run app $"UPDATE passkeys SET added_via = '{v}' WHERE id = '{id}'"
        | None -> ()
    }

let private addConnector (app: Fixtures.TestApp) (slug: string) (kind: ProviderKind) (issuer: string option) =
    task {
        let connector: Connector =
            { Slug = slug
              Kind = kind
              DisplayName = slug
              ClientId = "client-id"
              ClientSecretRef = ""
              ClientSecretHint = ""
              IssuerUrl = issuer
              AllowedEmailDomains = []
              PasskeyEnrolment = InvitationsOnly
              FediverseAccess = FediverseAccess.Any
              AppleKey = None
              MicrosoftAudience = None
              EmailTrust = (if kind = ProviderKind.Oidc then Some true else None)
              IsEnabled = true
              IsShared = true
              CreatedUtc = DateTimeOffset.UnixEpoch
              UpdatedUtc = DateTimeOffset.UnixEpoch }

        let! created = ConnectorStore.create app.DbConfig (app.Clock.GetUtcNow()) connector ("local:ciphertext", "cret")
        Assert.Equal(ConnectorStore.Created, created)
    }

let private fp = Linking.fingerprint

let private removePath (userId: string) (fingerprint: string) =
    $"/admin/users/{userId}/links/{fingerprint}/remove"

/// Sam: GitHub (linked 2026-01-01, used 2026-01-02) and Google (linked
/// 2026-02-01, used 2026-02-05); a passkey added through GitHub, one added
/// through that passkey, one of Google's, and two of unknown origin, one from
/// before the GitHub link and one after it.
let private seedSam (app: Fixtures.TestApp) =
    task {
        let github = fp "github" "g-1"
        let google = fp "google" "o-1"
        do! addUser app "sam" "Sam"
        do! addLink app "sam" "github" "g-1" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"
        do! addLink app "sam" "google" "o-1" "2026-02-01T00:00:00Z" "2026-02-05T00:00:00Z"
        do! addPasskey app "sam" "via" "2026-01-10T00:00:00Z" (Some $"link:{github}")
        do! addPasskey app "sam" "trans" "2026-01-11T00:00:00Z" (Some "passkey:via")
        do! addPasskey app "sam" "other" "2026-02-10T00:00:00Z" (Some $"link:{google}")
        do! addPasskey app "sam" "before" "2025-12-01T00:00:00Z" None
        do! addPasskey app "sam" "after" "2026-03-01T00:00:00Z" None
    }

let private operatorFor (app: Fixtures.TestApp) =
    task {
        let operator = Browser(app.Client)
        do! claimOperator app operator
        return operator
    }

let private expectCount (app: Fixtures.TestApp) (sql: string) (expected: int) =
    task {
        let! actual = count app sql
        Assert.Equal(expected, actual)
    }

/// The `<li>` of one link's row, to the end of its list item.
let private rowOf (html: string) (fingerprint: string) =
    let start = html.IndexOf $"data-user-link=\"{fingerprint}\""
    Assert.True(start >= 0, "the row is drawn")
    html.Substring(start, html.IndexOf("</li>", start) - start)

[<Fact>]
let ``the list is drawn with both labels and dates, and the passkey list has its label`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! seedSam app

        let! html = operator.GetHtml "/admin/users"

        Assert.Contains("data-user-links", html)
        Assert.Contains("Linked accounts", html)
        Assert.Contains("data-user-passkeys", html)
        Assert.Contains(">Passkeys<", html)

        let github = rowOf html (fp "github" "g-1")
        Assert.Contains("GitHub", github)
        Assert.Contains("Linked 2026-01-01", github)
        Assert.Contains("Last used 2026-01-02", github)
        Assert.Contains("data-user-link-remove", github)
        Assert.Contains("Remove link", github)
        // It asks for the confirm block: no script confirm on it.
        Assert.DoesNotContain("data-confirm", github)
        Assert.Contains("action=\"" + removePath "sam" (fp "github" "g-1") + "\"", github)

        let google = rowOf html (fp "google" "o-1")
        Assert.Contains("Google", google)
        Assert.Contains("Linked 2026-02-01", google)
        Assert.Contains("Last used 2026-02-05", google)

        // Neither the subject nor the stored address is on the page.
        Assert.DoesNotContain("g-1", html.Replace(fp "github" "g-1", ""))

        // The passkey Remove says what else it does.
        Assert.Contains("anything signed in with it is signed out", html)
        Assert.DoesNotContain("data-user-link-confirm", html)
    }

[<Fact>]
let ``one link and no passkey draws no list, and a passkey-only user has the passkey label alone`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addUser app "solo" "Solo"
        do! addLink app "solo" "github" "g-solo" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"
        do! addUser app "keys" "Keys"
        do! addPasskey app "keys" "k1" "2026-01-01T00:00:00Z" None
        do! addPasskey app "keys" "k2" "2026-01-02T00:00:00Z" None

        let! html = operator.GetHtml "/admin/users"

        Assert.DoesNotContain("data-user-links", html)
        Assert.DoesNotContain("Linked accounts", html)
        Assert.DoesNotContain("data-user-link-remove", html)
        // The pill stays.
        Assert.Contains("data-linked-provider=\"github\"", html)
        // Two passkeys: the passkey list is drawn under its label.
        Assert.Contains(">Passkeys<", html)
    }

[<Fact>]
let ``one link and one passkey is two ways in, so the list is drawn`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addUser app "duo" "Duo"
        do! addLink app "duo" "github" "g-duo" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"
        do! addPasskey app "duo" "d1" "2026-01-01T00:00:00Z" None

        let! html = operator.GetHtml "/admin/users"

        Assert.Contains("data-user-links", html)
    }

[<Fact>]
let ``the first post changes nothing and answers 422 with the block for that row`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! seedSam app
        let github = fp "github" "g-1"

        let! response = operator.PostForm("/admin/users", removePath "sam" github, [ "search", "" ])
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        Assert.Contains("data-user-link-confirm", html)

        // Question, with the label and the name.
        Assert.Contains("Remove the GitHub link from Sam?", html)
        Assert.Contains("becomes a new user", html)
        Assert.Contains("This cannot be undone.", html)

        // The block replaces that row's button; the other row keeps its own.
        let block = html.Substring(html.IndexOf "data-user-link-confirm")
        let githubRow = rowOf html github
        Assert.DoesNotContain("data-user-link-remove", githubRow)
        Assert.Contains("data-user-link-remove", rowOf html (fp "google" "o-1"))

        // Removed too: the passkey added through the link and the one added through it.
        let gone = block.Substring(block.IndexOf "data-user-link-gone-passkeys")
        let goneList = gone.Substring(0, gone.IndexOf "</ul>")
        Assert.Contains("These passkeys were added while signed in with it, and are removed too:", block)
        Assert.Contains("Key via, added 2026-01-10", goneList)
        Assert.Contains("Key trans, added 2026-01-11", goneList)
        Assert.DoesNotContain("Key other", goneList)
        Assert.DoesNotContain("Key after", goneList)

        // To check: unknown origin, added after the link; not the older one.
        let check = block.Substring(block.IndexOf "data-user-link-check-passkeys")
        let checkList = check.Substring(0, check.IndexOf "</ul>")
        Assert.Contains("These passkeys were added since 2026-01-01 and may be that person", block)
        Assert.Contains("Key after, added 2026-03-01", checkList)
        Assert.DoesNotContain("Key before", checkList)
        Assert.DoesNotContain("Key via", checkList)

        // Its button posts the confirmation.
        Assert.Contains("name=\"confirm\"", block)
        Assert.Contains("value=\"remove\"", block)

        // Nothing changed.
        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE user_id = 'sam'" 2
        do! expectCount app "SELECT count(*) FROM passkeys WHERE user_id = 'sam'" 5
        do! expectCount app "SELECT count(*) FROM security_events WHERE event_type = 'user_unlinked'" 0
        do! expectCount app "SELECT count(*) FROM removed_links" 0
    }

[<Fact>]
let ``the block has no lists when there are no passkeys to name`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addUser app "two" "Two"
        do! addLink app "two" "github" "g-a" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"
        do! addLink app "two" "google" "o-a" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"

        let! response = operator.PostForm("/admin/users", removePath "two" (fp "github" "g-a"), [ "search", "" ])
        let! html = response.Content.ReadAsStringAsync()

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode)
        Assert.Contains("data-user-link-confirm", html)
        Assert.DoesNotContain("data-user-link-gone-passkeys", html)
        Assert.DoesNotContain("data-user-link-check-passkeys", html)
    }

[<Fact>]
let ``Cancel keeps the search, and the forms carry it`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! seedSam app
        let github = fp "github" "g-1"

        let! searched = operator.PostForm("/admin/users", removePath "sam" github, [ "search", "Sam" ])
        let! html = searched.Content.ReadAsStringAsync()
        let block = html.Substring(html.IndexOf "data-user-link-confirm")

        Assert.Contains("href=\"/admin/users?search=Sam\"", block)
        Assert.Contains("name=\"search\" value=\"Sam\"", block)
        // The other row's button, still a button, carries it too.
        Assert.Contains("name=\"search\" value=\"Sam\"", rowOf html (fp "google" "o-1"))

        let! plain = operator.PostForm("/admin/users", removePath "sam" github, [ "search", "" ])
        let! plainHtml = plain.Content.ReadAsStringAsync()
        Assert.Contains("href=\"/admin/users\"", plainHtml.Substring(plainHtml.IndexOf "data-user-link-confirm"))
    }

[<Fact>]
let ``the confirm post removes the link and its passkeys, records the event and says so`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addConnector app "github" ProviderKind.GitHub None
        do! seedSam app
        let github = fp "github" "g-1"

        let! response =
            operator.PostForm("/admin/users", removePath "sam" github, [ "confirm", "remove"; "search", "" ])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("user-link-removed", location response)

        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE user_id = 'sam'" 1
        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE provider_kind = 'github'" 0
        // via and trans went; other, before and after stay.
        do! expectCount app "SELECT count(*) FROM passkeys WHERE user_id = 'sam'" 3
        do! expectCount app "SELECT count(*) FROM passkeys WHERE id IN ('via', 'trans')" 0

        // One event, naming the connector by slug and the count; the actor is the operator.
        do! expectCount app "SELECT count(*) FROM security_events WHERE event_type = 'user_unlinked'" 1

        let! row =
            scalar
                app
                "SELECT outcome || '|' || actor || '|' || user_id || '|' || ifnull(connector_slug, '-') || '|' || detail
                 FROM security_events WHERE event_type = 'user_unlinked'"
                []

        Assert.Equal("success|operator|sam|github|kind=github;passkeys=2", row)

        // Neither the subject nor the address is in any column of any event.
        let! leaked =
            scalar
                app
                "SELECT count(*) FROM security_events
                 WHERE ifnull(detail, '') || ifnull(user_id, '') || ifnull(connector_slug, '') || ifnull(remote_ip, '')
                       LIKE '%g-1%'
                    OR ifnull(detail, '') LIKE '%@%'"
                []

        Assert.Equal("0", leaked)

        // The notice, as a success.
        let! page = operator.GetHtml(location response)
        Assert.Contains("Link removed.", page)
        Assert.Contains("um-alert-success", page)
        Assert.DoesNotContain("um-alert-warning", page)
    }

[<Fact>]
let ``an OpenID Connect link is recorded with the connector that holds its issuer`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addConnector app "home" ProviderKind.Oidc (Some "https://id.home.example")
        do! addConnector app "work" ProviderKind.Oidc (Some "https://id.work.example")
        do! addUser app "oi" "Oi"
        do! addLink app "oi" "oidc" "https://id.work.example#abc" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"
        do! addLink app "oi" "oidc" "https://id.home.example#abc" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"

        let! response =
            operator.PostForm(
                "/admin/users",
                removePath "oi" (fp "oidc" "https://id.work.example#abc"),
                [ "confirm", "remove"; "search", "" ]
            )

        Assert.Contains("user-link-removed", location response)

        let! row =
            scalar
                app
                "SELECT connector_slug || '|' || detail FROM security_events WHERE event_type = 'user_unlinked'"
                []

        Assert.Equal("work|kind=oidc;passkeys=0", row)
    }

[<Fact>]
let ``two connectors of one kind leave the slug empty`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addConnector app "github" ProviderKind.GitHub None
        do! addConnector app "github-2" ProviderKind.GitHub None
        do! addUser app "gg" "Gg"
        do! addLink app "gg" "github" "g-x" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"
        do! addLink app "gg" "google" "o-x" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"

        let! response =
            operator.PostForm("/admin/users", removePath "gg" (fp "github" "g-x"), [ "confirm", "remove"; "search", "" ])

        Assert.Contains("user-link-removed", location response)

        let! slug =
            scalar app "SELECT ifnull(connector_slug, 'none') FROM security_events WHERE event_type = 'user_unlinked'" []

        Assert.Equal("none", slug)
    }

[<Fact>]
let ``the only way in is refused with the user's name and nothing changes`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addUser app "last" "Lee"
        do! addLink app "last" "github" "g-last" "2026-01-01T00:00:00Z" "2026-01-02T00:00:00Z"

        let! response =
            operator.PostForm(
                "/admin/users",
                removePath "last" (fp "github" "g-last"),
                [ "confirm", "remove"; "search", "" ]
            )

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("user-link-last", location response)

        let! page = operator.GetHtml(location response)
        Assert.Contains("That is Lee's only way to sign in, so it was not removed.", page.Replace("&#39;", "'"))
        Assert.Contains("um-alert-warning", page)

        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE user_id = 'last'" 1
        do! expectCount app "SELECT count(*) FROM security_events WHERE event_type = 'user_unlinked'" 0
        do! expectCount app "SELECT count(*) FROM removed_links" 0
    }

[<Fact>]
let ``the refusal says the user when the user is not among the rows`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! addUser app "other" "Ola"

        let! page = operator.GetHtml "/admin/users?msg=user-link-last&user=nobody"

        Assert.Contains("That is the user's only way to sign in", page.Replace("&#39;", "'"))
        Assert.Contains("um-alert-warning", page)
    }

[<Fact>]
let ``an unknown fingerprint is a warning, on the first post and on the confirm`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! seedSam app
        let unknown = fp "github" "not-linked"

        for fields in [ [ "search", "" ]; [ "confirm", "remove"; "search", "" ] ] do
            let! response = operator.PostForm("/admin/users", removePath "sam" unknown, fields)

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
            Assert.Contains("user-link-not-found", location response)

            let! page = operator.GetHtml(location response)
            Assert.Contains("That link was already removed.", page)
            Assert.Contains("um-alert-warning", page)

        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE user_id = 'sam'" 2
        do! expectCount app "SELECT count(*) FROM passkeys WHERE user_id = 'sam'" 5
        do! expectCount app "SELECT count(*) FROM security_events WHERE event_type = 'user_unlinked'" 0
    }

[<Fact>]
let ``an unknown user is a warning`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! seedSam app

        let! response =
            operator.PostForm(
                "/admin/users",
                removePath "nobody" (fp "github" "g-1"),
                [ "confirm", "remove"; "search", "" ]
            )

        Assert.Contains("user-not-found", location response)

        let! page = operator.GetHtml(location response)
        Assert.Contains("That user no longer exists.", page)
        Assert.Contains("um-alert-warning", page)
        Assert.DoesNotContain("um-alert-success", page)
    }

[<Fact>]
let ``a post without the antiforgery token is refused and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! operator = operatorFor app
        do! seedSam app

        let! response =
            operator.PostWithoutToken(removePath "sam" (fp "github" "g-1"), [ "confirm", "remove"; "search", "" ])

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE user_id = 'sam'" 2
        do! expectCount app "SELECT count(*) FROM security_events WHERE event_type = 'user_unlinked'" 0
    }

[<Fact>]
let ``a post without an operator session is sent to sign in and changes nothing`` () =
    task {
        use! app = Fixtures.createApp ()
        let! _ = operatorFor app
        do! seedSam app
        let stranger = Browser(app.Client)

        let! response =
            stranger.PostWithoutToken(removePath "sam" (fp "github" "g-1"), [ "confirm", "remove"; "search", "" ])

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode)
        Assert.Contains("/admin/signin", location response)
        do! expectCount app "SELECT count(*) FROM linked_accounts WHERE user_id = 'sam'" 2
        do! expectCount app "SELECT count(*) FROM security_events WHERE event_type = 'user_unlinked'" 0
    }
