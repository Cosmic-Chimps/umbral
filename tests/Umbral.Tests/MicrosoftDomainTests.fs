/// The pure rules of the Microsoft accounts connector (specs/016 data-model and
/// research R2-R4): the issuers, who each audience admits, the tenant and client
/// ID parsing, the canonical `tid`, and the subject.
module Umbral.Tests.MicrosoftDomainTests

open System
open Xunit
open Umbral.Domains.Connectors
open Umbral.Domains.Microsoft

let private tenantA = Guid.Parse "0f8fad5b-d9cb-469f-a165-70867728950e"
let private tenantB = Guid.Parse "6ba7b810-9dad-11d1-80b4-00c04fd430c8"
let private oid = Guid.Parse "3f2504e0-4f89-11d3-9a0c-0305e82c3301"

let private everyAudience =
    [ PersonalAccounts; OneOrganization tenantA; AnyOrganization; PersonalAndAnyOrganization ]

[<Fact>]
let ``the kind is microsoft, built in, with the scopes and flags of the contract`` () =
    Assert.Equal("microsoft", Microsoft.Value)
    Assert.Equal("Microsoft", Microsoft.DisplayName)
    Assert.Equal(Some Microsoft, parseKind "microsoft")
    Assert.DoesNotContain(Microsoft, allKinds)

    let endpoints = endpointsFor Microsoft
    Assert.Equal("", endpoints.AuthorizationUrl)
    Assert.Equal("", endpoints.TokenUrl)
    Assert.Equal("", endpoints.UserInfoUrl)
    Assert.Equal<string list>([ "openid"; "email"; "profile" ], endpoints.Scopes)
    Assert.True endpoints.SupportsPkce
    Assert.True endpoints.UsesNonce
    Assert.False endpoints.AnswersByFormPost

[<Fact>]
let ``the caveat names the secret's expiry`` () =
    Assert.Equal(
        Some
            "A Microsoft client secret expires: after 6 months by default, 24 months at most. Note the date. Once it has passed, nobody can sign in through Microsoft until you replace the secret here.",
        setupCaveat Microsoft
    )

[<Fact>]
let ``the personal tenant, the authority and the issuer template are Microsoft's`` () =
    Assert.Equal("9188040d-6c67-4c5b-b112-36a304b66dad", personalTenant.ToString "D")
    Assert.Equal("https://login.microsoftonline.com", authority)
    Assert.Equal("https://login.microsoftonline.com/{tenantid}/v2.0", issuerTemplate)

[<Fact>]
let ``issuerFor replaces the placeholder with the tid`` () =
    Assert.Equal(
        "https://login.microsoftonline.com/0f8fad5b-d9cb-469f-a165-70867728950e/v2.0",
        issuerFor "0f8fad5b-d9cb-469f-a165-70867728950e"
    )

[<Fact>]
let ``stored and segment are consumers, the tenant GUID in lower case, organizations and common`` () =
    Assert.Equal("consumers", stored PersonalAccounts)
    Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", stored (OneOrganization tenantA))
    Assert.Equal("organizations", stored AnyOrganization)
    Assert.Equal("common", stored PersonalAndAnyOrganization)

    for audience in everyAudience do
        Assert.Equal(stored audience, segment audience)

[<Fact>]
let ``discoveryIssuer is the personal tenant's issuer, the tenant's issuer or the template`` () =
    Assert.Equal(
        "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0",
        discoveryIssuer PersonalAccounts
    )

    Assert.Equal(
        "https://login.microsoftonline.com/0f8fad5b-d9cb-469f-a165-70867728950e/v2.0",
        discoveryIssuer (OneOrganization tenantA)
    )

    Assert.Equal("https://login.microsoftonline.com/{tenantid}/v2.0", discoveryIssuer AnyOrganization)
    Assert.Equal("https://login.microsoftonline.com/{tenantid}/v2.0", discoveryIssuer PersonalAndAnyOrganization)

[<Fact>]
let ``documentBase is the authority, the segment and v2.0`` () =
    Assert.Equal("https://login.microsoftonline.com/consumers/v2.0", documentBase PersonalAccounts)

    Assert.Equal(
        "https://login.microsoftonline.com/0f8fad5b-d9cb-469f-a165-70867728950e/v2.0",
        documentBase (OneOrganization tenantA)
    )

    Assert.Equal("https://login.microsoftonline.com/organizations/v2.0", documentBase AnyOrganization)
    Assert.Equal("https://login.microsoftonline.com/common/v2.0", documentBase PersonalAndAnyOrganization)

[<Fact>]
let ``personal accounts admit only the personal tenant`` () =
    Assert.True(admits PersonalAccounts personalTenant)
    Assert.False(admits PersonalAccounts tenantA)
    Assert.False(admits PersonalAccounts tenantB)

[<Fact>]
let ``one organization admits only that tenant`` () =
    Assert.True(admits (OneOrganization tenantA) tenantA)
    Assert.False(admits (OneOrganization tenantA) tenantB)
    Assert.False(admits (OneOrganization tenantA) personalTenant)

[<Fact>]
let ``any organization admits every tenant but the personal one`` () =
    Assert.True(admits AnyOrganization tenantA)
    Assert.True(admits AnyOrganization tenantB)
    Assert.False(admits AnyOrganization personalTenant)

[<Fact>]
let ``personal and any organization admits every tenant`` () =
    Assert.True(admits PersonalAndAnyOrganization personalTenant)
    Assert.True(admits PersonalAndAnyOrganization tenantA)
    Assert.True(admits PersonalAndAnyOrganization tenantB)

[<Fact>]
let ``parseStoredAudience is the inverse of stored`` () =
    for audience in everyAudience @ [ OneOrganization tenantB; OneOrganization personalTenant ] do
        Assert.Equal(Some audience, parseStoredAudience (stored audience))

[<Theory>]
[<InlineData("")>]
[<InlineData("Consumers")>]
[<InlineData("everyone")>]
[<InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E")>]
[<InlineData("{0f8fad5b-d9cb-469f-a165-70867728950e}")>]
[<InlineData("0f8fad5bd9cb469fa16570867728950e")>]
[<InlineData(" 0f8fad5b-d9cb-469f-a165-70867728950e")>]
[<InlineData("contoso.onmicrosoft.com")>]
let ``parseStoredAudience refuses anything else`` (input: string) =
    Assert.Equal(None, parseStoredAudience input)

[<Fact>]
let ``parseStoredAudience refuses null`` () =
    Assert.Equal(None, parseStoredAudience null)

[<Theory>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")>]
[<InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E")>]
[<InlineData("  0f8fad5b-d9cb-469f-a165-70867728950e\t")>]
let ``parseTenantId and parseClientId accept a GUID in any case, trimmed`` (input: string) =
    Assert.Equal(Some tenantA, parseTenantId input)
    Assert.Equal(Some tenantA, parseClientId input)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("contoso.onmicrosoft.com")>]
[<InlineData("{0f8fad5b-d9cb-469f-a165-70867728950e}")>]
[<InlineData("0f8fad5bd9cb469fa16570867728950e")>]
[<InlineData("(0f8fad5b-d9cb-469f-a165-70867728950e)")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e0")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950g")>]
let ``parseTenantId and parseClientId refuse what is not a D-form GUID`` (input: string) =
    Assert.Equal(None, parseTenantId input)
    Assert.Equal(None, parseClientId input)

[<Fact>]
let ``parseTenantId and parseClientId refuse null`` () =
    Assert.Equal(None, parseTenantId null)
    Assert.Equal(None, parseClientId null)

[<Fact>]
let ``canonicalTid accepts exactly the lower-case D form of a GUID`` () =
    Assert.Equal(Some tenantA, canonicalTid "0f8fad5b-d9cb-469f-a165-70867728950e")
    Assert.Equal(Some personalTenant, canonicalTid "9188040d-6c67-4c5b-b112-36a304b66dad")

[<Theory>]
[<InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E")>]
[<InlineData("0f8fad5b-D9CB-469f-a165-70867728950e")>]
[<InlineData("{0f8fad5b-d9cb-469f-a165-70867728950e}")>]
[<InlineData("0f8fad5bd9cb469fa16570867728950e")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e ")>]
[<InlineData(" 0f8fad5b-d9cb-469f-a165-70867728950e")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e\n")>]
[<InlineData("")>]
[<InlineData("contoso.onmicrosoft.com")>]
let ``canonicalTid refuses every other spelling`` (input: string) =
    Assert.Equal(None, canonicalTid input)

[<Fact>]
let ``canonicalTid refuses null`` () = Assert.Equal(None, canonicalTid null)

[<Fact>]
let ``microsoftSubject is tid hash oid in lower case`` () =
    Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e#3f2504e0-4f89-11d3-9a0c-0305e82c3301", microsoftSubject tenantA oid)

[<Fact>]
let ``parseMicrosoftSubject is the inverse of microsoftSubject`` () =
    Assert.Equal(Some(tenantA, oid), parseMicrosoftSubject (microsoftSubject tenantA oid))
    Assert.Equal(Some(personalTenant, oid), parseMicrosoftSubject (microsoftSubject personalTenant oid))

[<Theory>]
[<InlineData("")>]
[<InlineData("#")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e#")>]
[<InlineData("#3f2504e0-4f89-11d3-9a0c-0305e82c3301")>]
[<InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E#3f2504e0-4f89-11d3-9a0c-0305e82c3301")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e#3F2504E0-4F89-11D3-9A0C-0305E82C3301")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e#3f2504e0-4f89-11d3-9a0c-0305e82c3301#x")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e#not-a-guid")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e 3f2504e0-4f89-11d3-9a0c-0305e82c3301")>]
[<InlineData("0f8fad5b-d9cb-469f-a165-70867728950e#3f2504e0-4f89-11d3-9a0c-0305e82c3301 ")>]
let ``parseMicrosoftSubject refuses anything but two canonical GUIDs`` (input: string) =
    Assert.Equal(None, parseMicrosoftSubject input)

[<Fact>]
let ``parseMicrosoftSubject refuses null`` () = Assert.Equal(None, parseMicrosoftSubject null)
