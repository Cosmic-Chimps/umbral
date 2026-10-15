/// The allowed-email-domains rule, branch by branch (specs/006). Pure: no
/// database, no host. It is the one place that decides who may sign in by the
/// domain of their email, so every refusal path and every spelling the operator
/// might type has its own case.
module Umbral.Tests.EmailDomainsTests

open Xunit
open Umbral.Domains.EmailDomains

let private ok (input: string) =
    match parse input with
    | Ok list -> list
    | Error error -> failwith $"'{input}' should parse but gave {error}"

// ---------------------------------------------------------------------------
// parse
// ---------------------------------------------------------------------------

[<Fact>]
let ``separators are commas, spaces, tabs and new lines`` () =
    Assert.Equal<string list>([ "acme.com"; "example.org"; "corp.net"; "a.io" ], ok "acme.com,example.org corp.net\n\ta.io")
    Assert.Equal<string list>([ "acme.com"; "example.org" ], ok "acme.com,\r\n  example.org ,")

[<Fact>]
let ``a leading at sign and a trailing dot are dropped`` () =
    Assert.Equal<string list>([ "acme.com"; "example.org" ], ok "@acme.com, example.org.")

[<Fact>]
let ``upper case is lowered`` () =
    Assert.Equal<string list>([ "acme.com" ], ok "ACME.Com")

[<Fact>]
let ``duplicates are dropped and the first order is kept`` () =
    Assert.Equal<string list>([ "b.com"; "a.com" ], ok "b.com a.com B.COM @a.com. b.com")

[<Fact>]
let ``an international name and its ASCII form are the same entry`` () =
    Assert.Equal<string list>([ "xn--mnchen-3ya.de" ], ok "münchen.de")
    Assert.Equal<string list>([ "xn--mnchen-3ya.de" ], ok "münchen.de xn--mnchen-3ya.de MÜNCHEN.DE")

[<Fact>]
let ``empty or blank input is an empty list`` () =
    Assert.Equal(Ok [], parse "")
    Assert.Equal(Ok [], parse "  \n\t , ")

[<Theory>]
[<InlineData("*")>]
[<InlineData("*.acme.com")>]
[<InlineData("acme.*")>]
let ``a wildcard is refused with its own error`` (entry: string) =
    Assert.Equal(Error(Wildcard entry), parse $"good.com {entry}")

[<Theory>]
[<InlineData("localhost")>]
[<InlineData("1.2.3.4")>]
[<InlineData("-a.com")>]
[<InlineData("a-.com")>]
[<InlineData("a..com")>]
[<InlineData("a_b.com")>]
[<InlineData("com")>]
[<InlineData("not a@domain")>]
[<InlineData("a.com/path")>]
let ``an entry that is not a domain is refused and named`` (entry: string) =
    // `not a@domain` splits into two entries; the first refused one is named.
    let expected =
        match entry with
        | "not a@domain" -> "not"
        | other -> other

    Assert.Equal(Error(NotADomain expected), parse $"good.com {entry}")

[<Fact>]
let ``a label of 64 characters is refused and 63 is accepted`` () =
    let sixtyThree = String.replicate 63 "a"
    let sixtyFour = String.replicate 64 "a"
    Assert.Equal<string list>([ $"{sixtyThree}.com" ], ok $"{sixtyThree}.com")
    Assert.Equal(Error(NotADomain $"{sixtyFour}.com"), parse $"{sixtyFour}.com")

[<Fact>]
let ``the first refused entry is the one named`` () =
    Assert.Equal(Error(NotADomain "localhost"), parse "acme.com localhost 1.2.3.4")

// ---------------------------------------------------------------------------
// serialise, deserialise, display
// ---------------------------------------------------------------------------

[<Fact>]
let ``the stored form is the ASCII domains joined by one space`` () =
    let list = ok "acme.com münchen.de"
    Assert.Equal("acme.com xn--mnchen-3ya.de", serialise list)
    Assert.Equal<string list>(list, deserialise (serialise list))
    Assert.Equal<string list>([], deserialise "")

[<Fact>]
let ``display shows the Unicode form joined by comma and space`` () =
    Assert.Equal("acme.com, münchen.de", display (ok "acme.com münchen.de"))
    Assert.Equal("", display [])

// ---------------------------------------------------------------------------
// applying
// ---------------------------------------------------------------------------

[<Fact>]
let ``the connector list wins over the instance list`` () =
    Assert.Equal(Rule(Connector, [ "a.com" ]), applying [ "a.com" ] [ "b.com" ])

[<Fact>]
let ``the instance list applies when the connector has none`` () =
    Assert.Equal(Rule(Instance, [ "b.com" ]), applying [] [ "b.com" ])

[<Fact>]
let ``no list anywhere is no rule`` () =
    Assert.Equal(NoRule, applying [] [])

[<Fact>]
let ``a connector list alone applies at the connector level`` () =
    Assert.Equal(Rule(Connector, [ "a.com"; "c.org" ]), applying [ "a.com"; "c.org" ] [])

// ---------------------------------------------------------------------------
// domainOf
// ---------------------------------------------------------------------------

[<Theory>]
[<InlineData("ann@acme.com", "acme.com")>]
[<InlineData("ERIN@ACME.COM", "acme.com")>]
[<InlineData("ann@acme.com.", "acme.com")>]
[<InlineData("a@b@acme.com", "acme.com")>]
[<InlineData("\"a@b\"@acme.com", "acme.com")>]
[<InlineData("  ann@acme.com  ", "acme.com")>]
[<InlineData("ann@münchen.de", "xn--mnchen-3ya.de")>]
[<InlineData("ann@MÜNCHEN.de", "xn--mnchen-3ya.de")>]
let ``the domain is what follows the last at sign, in ASCII and lower case`` (email: string, expected: string) =
    Assert.Equal(Some expected, domainOf (Some email))

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("ann")>]
[<InlineData("ann@")>]
[<InlineData("ann@  ")>]
let ``no domain part gives none`` (email: string) =
    Assert.Equal(None, domainOf (Some email))
    Assert.Equal(None, domainOf None)

// ---------------------------------------------------------------------------
// check
// ---------------------------------------------------------------------------

let private acme = Rule(Connector, [ "acme.com" ])

[<Fact>]
let ``a verified address at a listed domain is admitted`` () =
    Assert.Equal(Admitted, check acme (Some "ann@acme.com") true)

[<Fact>]
let ``the case of the address is ignored`` () =
    Assert.Equal(Admitted, check acme (Some "ERIN@ACME.COM") true)

[<Fact>]
let ``an international address matches its ASCII listing`` () =
    Assert.Equal(Admitted, check (Rule(Instance, [ "xn--mnchen-3ya.de" ])) (Some "ann@münchen.de") true)

[<Fact>]
let ``a subdomain is not the listed domain`` () =
    Assert.Equal(Refused(Connector, DomainNotAllowed(Some "eu.acme.com")), check acme (Some "dave@eu.acme.com") true)

[<Fact>]
let ``a domain that only ends like a listed one is refused`` () =
    Assert.Equal(Refused(Connector, DomainNotAllowed(Some "notacme.com")), check acme (Some "x@notacme.com") true)

[<Fact>]
let ``another domain is refused and names it`` () =
    Assert.Equal(Refused(Connector, DomainNotAllowed(Some "gmail.com")), check acme (Some "bob@gmail.com") true)

[<Fact>]
let ``an unverified address at a listed domain is refused`` () =
    Assert.Equal(Refused(Connector, EmailNotVerified), check acme (Some "carol@acme.com") false)

[<Fact>]
let ``no email is refused`` () =
    Assert.Equal(Refused(Connector, NoEmail), check acme None true)
    Assert.Equal(Refused(Connector, NoEmail), check acme (Some "") true)
    Assert.Equal(Refused(Connector, NoEmail), check acme (Some "no-at-sign") true)
    Assert.Equal(Refused(Connector, NoEmail), check acme (Some "ann@") true)

[<Fact>]
let ``no email wins over not verified, and not verified wins over the domain`` () =
    Assert.Equal(Refused(Connector, NoEmail), check acme None false)
    Assert.Equal(Refused(Connector, EmailNotVerified), check acme (Some "bob@gmail.com") false)

[<Fact>]
let ``the refusal carries the level of the rule`` () =
    Assert.Equal(Refused(Instance, DomainNotAllowed(Some "gmail.com")), check (Rule(Instance, [ "acme.com" ])) (Some "bob@gmail.com") true)

[<Fact>]
let ``a domain the mapping refuses counts as not allowed`` () =
    let tooLong = String.replicate 64 "a"
    Assert.Equal(Refused(Connector, DomainNotAllowed None), check acme (Some $"x@{tooLong}.com") true)

[<Fact>]
let ``no rule always admits`` () =
    Assert.Equal(Admitted, check NoRule (Some "bob@gmail.com") true)
    Assert.Equal(Admitted, check NoRule (Some "bob@gmail.com") false)
    Assert.Equal(Admitted, check NoRule None false)
    Assert.Equal(Admitted, check NoRule (Some "") true)

// ---------------------------------------------------------------------------
// checkDomain: the same rule, for something that remembers only the domain
// ---------------------------------------------------------------------------

[<Fact>]
let ``a remembered domain is admitted when listed and refused otherwise`` () =
    Assert.Equal(Admitted, checkDomain acme (Some "acme.com"))
    Assert.Equal(Refused(Connector, DomainNotAllowed(Some "gmail.com")), checkDomain acme (Some "gmail.com"))
    Assert.Equal(Refused(Connector, DomainNotAllowed(Some "eu.acme.com")), checkDomain acme (Some "eu.acme.com"))

[<Fact>]
let ``no remembered domain counts as no email`` () =
    Assert.Equal(Refused(Connector, NoEmail), checkDomain acme None)
    Assert.Equal(Refused(Instance, NoEmail), checkDomain (Rule(Instance, [ "acme.com" ])) None)

[<Fact>]
let ``checkDomain with no rule admits whatever was remembered`` () =
    Assert.Equal(Admitted, checkDomain NoRule None)
    Assert.Equal(Admitted, checkDomain NoRule (Some "gmail.com"))
