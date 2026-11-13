module Umbral.Tests.SecurityEventsTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Extensions.Time.Testing
open Xunit
open Umbral.Infrastructure
open Umbral.Infrastructure.SecurityEvents

let private withDb (body: Database.DbConfig -> Task<unit>) =
    task {
        let path = Path.Combine(Path.GetTempPath(), "umbral-ev-" + Guid.NewGuid().ToString "N" + ".db")
        let config: Database.DbConfig = { DataSource = path }

        try
            do! Database.initializeSchema config
            do! body config
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            for suffix in [ ""; "-wal"; "-shm" ] do
                if File.Exists(path + suffix) then File.Delete(path + suffix)
    }

let private rows (config: Database.DbConfig) (sql: string) =
    task {
        use! connection = Database.openConnection config
        use command = connection.CreateCommand()
        command.CommandText <- sql
        let! value = command.ExecuteScalarAsync()
        return if isNull value then "" else string value
    }

[<Fact>]
let ``an event is written with its fields`` () =
    withDb (fun config ->
        task {
            do!
                record
                    config
                    TimeProvider.System
                    { event SignInSucceeded Success User with
                        UserId = Some "u1"
                        ConnectorSlug = Some "discord" }

            let! stored =
                rows config "SELECT event_type || '|' || outcome || '|' || actor || '|' || user_id || '|' || connector_slug FROM security_events"

            Assert.Equal("sign_in_succeeded|success|user|u1|discord", stored)
        })

[<Fact>]
let ``absent fields are stored as null, not as an empty string`` () =
    withDb (fun config ->
        task {
            do! record config TimeProvider.System (event OperatorSignInFailed Failure Operator)

            // An operator querying this table by hand should be able to tell
            // "no client was involved" from "the client id was blank".
            let! nulls =
                rows
                    config
                    "SELECT (user_id IS NULL) + (connector_slug IS NULL) + (client_id IS NULL) + (remote_ip IS NULL) + (detail IS NULL) FROM security_events"

            Assert.Equal("5", nulls)
        })

[<Fact>]
let ``an event rolls back with the change it describes`` () =
    withDb (fun config ->
        task {
            use! connection = Database.openConnection config
            use transaction = connection.BeginTransaction()

            do!
                recordIn
                    connection
                    transaction
                    TimeProvider.System
                    { event UserDisabled Success Operator with UserId = Some "u1" }

            do! transaction.RollbackAsync()

            // A user who was disabled but whose event vanished is worse than no
            // log at all: the operator would have no idea it happened.
            let! count = rows config "SELECT count(*) FROM security_events"
            Assert.Equal("0", count)
        })

[<Fact>]
let ``the clock is injectable so a test can assert the timestamp`` () =
    withDb (fun config ->
        task {
            let fixedNow = DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.Zero)
            let clock = new FakeTimeProvider(fixedNow)

            do! record config clock (event AppDeleted Success System)

            let! stored = rows config "SELECT occurred_utc FROM security_events"
            Assert.Equal(fixedNow, DateTimeOffset.Parse(stored, Globalization.CultureInfo.InvariantCulture))
        })

[<Fact>]
let ``timestamps are stored in a sortable round-trip format`` () =
    withDb (fun config ->
        task {
            do! record config TimeProvider.System (event SignInSucceeded Success User)
            let! stored = rows config "SELECT occurred_utc FROM security_events"

            // The table is read by an operator with sqlite3 and ORDER BY. A
            // locale-formatted date would sort wrongly and parse differently.
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", stored)
            Assert.EndsWith("+00:00", stored)
        })

[<Fact>]
let ``every event type has a distinct stored value`` () =
    let values = allEventTypes |> List.map (fun t -> t.Value)
    Assert.Equal<string list>(List.distinct values, values)

[<Fact>]
let ``stored values are snake case and free of surprises`` () =
    // These end up in a database an operator queries by hand and may script
    // against, so they are part of the contract, not an implementation detail.
    for t in allEventTypes do
        Assert.Matches(@"^[a-z][a-z_]*[a-z]$", t.Value)

[<Fact>]
let ``every event type can actually be written`` () =
    withDb (fun config ->
        task {
            for t in allEventTypes do
                do! record config TimeProvider.System (event t Success System)

            let! count = rows config "SELECT count(DISTINCT event_type) FROM security_events"
            Assert.Equal(string allEventTypes.Length, count)
        })

// ---------------------------------------------------------------------------
// The detail of a refused email domain (specs/006 research R10).
// ---------------------------------------------------------------------------

open Umbral.Domains.EmailDomains

[<Fact>]
let ``allowed_domains_changed has its stored value`` () =
    Assert.Equal("allowed_domains_changed", AllowedDomainsChanged.Value)

[<Fact>]
let ``the refusal detail is built from closed words and the domain`` () =
    Assert.Equal(
        "email_domain_refused;reason=domain_not_allowed;rule=connector;stage=callback;domain=gmail.com",
        refusalDetail (DomainNotAllowed(Some "gmail.com")) Connector Callback (Some "gmail.com")
    )

    Assert.Equal(
        "email_domain_refused;reason=email_not_verified;rule=instance;stage=authorize;domain=acme.com",
        refusalDetail EmailNotVerified Instance Authorize (Some "acme.com")
    )

    Assert.Equal(
        "email_domain_refused;reason=no_email;rule=instance;stage=token;domain=-",
        refusalDetail NoEmail Instance Token None
    )

[<Theory>]
[<InlineData("evil;rule=x")>]
[<InlineData("a b.com")>]
[<InlineData("UPPER.com")>]
[<InlineData("münchen.de")>]
[<InlineData("")>]
let ``a domain that is not in the normalised form is written as a dash`` (domain: string) =
    let detail = refusalDetail (DomainNotAllowed(Some domain)) Connector Callback (Some domain)
    Assert.EndsWith(";domain=-", detail)
    // Whatever the provider sent, the detail still has exactly its five parts.
    Assert.Equal(5, detail.Split(';').Length)

// ---------------------------------------------------------------------------
// Free text at the end of a detail (it must not add fields)
// ---------------------------------------------------------------------------

[<Fact>]
let ``safeText leaves plain text unchanged`` () =
    Assert.Equal("the connection failed (Connection refused)", safeText "the connection failed (Connection refused)")

[<Fact>]
let ``safeText turns the separators into harmless characters`` () =
    Assert.Equal("a,x:y", safeText "a;x=y")

[<Theory>]
[<InlineData("a\nb", "a b")>]
[<InlineData("a\r\nb", "a  b")>]
[<InlineData("a\tb", "a b")>]
[<InlineData("a\u001Fb", "a b")>]
let ``safeText turns control characters into a space`` (input: string, expected: string) =
    Assert.Equal(expected, safeText input)

[<Fact>]
let ``safeText of nothing is empty`` () =
    Assert.Equal("", safeText "")
    Assert.Equal("", safeText (Unchecked.defaultof<string>))

[<Fact>]
let ``safeText cuts long text to 300 characters ending in an ellipsis`` () =
    let result = safeText (String('x', 1000))
    Assert.Equal(300, result.Length)
    Assert.EndsWith("…", result)
    Assert.Equal(String('x', 299), result.Substring(0, 299))

[<Fact>]
let ``safeText does not leave a lone high surrogate where it cuts`` () =
    // Characters 298 and 299 are a surrogate pair, so the cut falls inside it.
    let text = String('x', 298) + "\U0001F600" + String('y', 100)
    let result = safeText text
    Assert.EndsWith("…", result)
    Assert.False(Char.IsHighSurrogate result[result.Length - 2])
    Assert.Equal(299, result.Length)

[<Fact>]
let ``a saved-disabled detail keeps a reason with separators as one field`` () =
    let detail =
        savedDisabledDetail "local" None (Some "the connection failed: idp.example;x=y")

    Assert.Equal("store=local;saved_disabled: the connection failed: idp.example,x:y", detail)
    Assert.Equal(2, detail.Split(';').Length)

[<Fact>]
let ``a saved-disabled detail names the audience before the reason`` () =
    let detail =
        savedDisabledDetail "local" (Some "common") (Some "tenant=evil;audience=x")

    Assert.Equal("store=local;audience=common;saved_disabled: tenant:evil,audience:x", detail)
    Assert.Equal(3, detail.Split(';').Length)

[<Fact>]
let ``a connector saved enabled has no saved-disabled field`` () =
    Assert.Equal("store=local", savedDisabledDetail "local" None None)
    Assert.Equal("store=local;audience=common", savedDisabledDetail "local" (Some "common") None)
