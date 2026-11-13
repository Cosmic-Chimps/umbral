/// The rules of the Microsoft accounts connector that need no I/O: the
/// endpoints, the issuers, who a choice of audience admits, the identity
/// (`tid` and `oid`) and the validation of what the operator types
/// (specs/016-microsoft-connector, research R1-R4).
module Umbral.Domains.Microsoft

open System
open Umbral.Domains.Connectors

/// The tenant ID of personal Microsoft accounts, from the `tid` row of
/// Microsoft's "ID token claims reference" (research R3). It is the issuer of the
/// `consumers` discovery document.
let personalTenant = Guid.Parse "9188040d-6c67-4c5b-b112-36a304b66dad"

/// The host of every Microsoft URL Umbral uses. Fixed: sovereign clouds are out
/// of scope (research R2).
let authority = "https://login.microsoftonline.com"

/// The issuer the `organizations` and `common` discovery documents name: a
/// template, `{tenantid}` is the tenant of whoever signs in (research R1, R2).
let issuerTemplate = "https://login.microsoftonline.com/{tenantid}/v2.0"

/// `issuerTemplate` with `{tenantid}` replaced. Built from this constant and a
/// `tid` that passed `canonicalTid`, never from a token's `iss` (research R2).
let issuerFor (tid: string) : string = issuerTemplate.Replace("{tenantid}", tid)

/// The GUID spelled the one way Umbral compares: `D`, lower case.
let private canonical (g: Guid) = g.ToString("D")

/// The stored form of an audience, `connectors.microsoft_audience`: `consumers`,
/// the tenant's GUID in lower case, `organizations` or `common`.
let stored (audience: MicrosoftAudience) : string =
    match audience with
    | PersonalAccounts -> "consumers"
    | OneOrganization tenant -> canonical tenant
    | AnyOrganization -> "organizations"
    | PersonalAndAnyOrganization -> "common"

/// The path segment of every Microsoft URL for this audience: the same string as
/// `stored` (research R3's table).
let segment (audience: MicrosoftAudience) : string = stored audience

/// The `issuer` the audience's discovery document must name, exactly: the
/// personal tenant's issuer, the tenant's issuer, or the template (research R1,
/// R3). Anything else is refused as `Wrong`.
let discoveryIssuer (audience: MicrosoftAudience) : string =
    match audience with
    | PersonalAccounts -> issuerFor (canonical personalTenant)
    | OneOrganization tenant -> issuerFor (canonical tenant)
    | AnyOrganization
    | PersonalAndAnyOrganization -> issuerTemplate

/// Whether the audience admits a person whose token has this `tid` (research R3's
/// last column). `organizations` admits every tenant except the personal one.
let admits (audience: MicrosoftAudience) (tid: Guid) : bool =
    match audience with
    | PersonalAccounts -> tid = personalTenant
    | OneOrganization tenant -> tid = tenant
    | AnyOrganization -> tid <> personalTenant
    | PersonalAndAnyOrganization -> true

/// The address whose `/.well-known/openid-configuration` is the audience's
/// discovery document: `<authority>/<segment>/v2.0`.
let documentBase (audience: MicrosoftAudience) : string =
    $"{authority}/{segment audience}/v2.0"

/// A GUID that is exactly the canonical spelling of itself: lower case, dashes,
/// 36 characters, no braces, no surrounding space.
let private tryCanonicalGuid (s: string | null) : Guid option =
    match s with
    | null -> None
    | s ->
        match Guid.TryParseExact(s, "D") with
        | true, g when canonical g = s -> Some g
        | _ -> None

/// The inverse of `stored`; None for anything else, including a GUID that is not
/// in its canonical lower-case form (what Umbral writes is what it reads back).
let parseStoredAudience (s: string | null) : MicrosoftAudience option =
    match s with
    | null -> None
    | "consumers" -> Some PersonalAccounts
    | "organizations" -> Some AnyOrganization
    | "common" -> Some PersonalAndAnyOrganization
    // The personal tenant is not "one organization": the form refuses it, so a row
    // that holds it was not written by Umbral, and it loads as unreadable (which
    // fails closed).
    | s -> tryCanonicalGuid s |> Option.filter (fun g -> g <> personalTenant) |> Option.map OneOrganization

/// A GUID in .NET's `D` spelling (dashes, no braces), either case, after
/// trimming: what an operator copies from the Entra admin centre.
let private parseGuid (input: string | null) : Guid option =
    match input with
    | null -> None
    | s ->
        match Guid.TryParseExact(s.Trim(), "D") with
        | true, g -> Some g
        | _ -> None

/// The tenant ID the operator typed for "one organization" (research R3). A GUID
/// only, not a domain name: tokens carry the GUID.
let parseTenantId (input: string | null) : Guid option = parseGuid input

/// The Application (client) ID the operator typed: the same GUID rule.
let parseClientId (input: string | null) : Guid option = parseGuid input

/// The `tid` of an `id_token`, accepted only when it is exactly the canonical
/// lower-case spelling of its own GUID (36 characters). It is read before the
/// token is validated, only to build the expected issuer, so nothing else may
/// pass (research R2, step 2).
let canonicalTid (s: string | null) : Guid option = tryCanonicalGuid s

/// The subject of a person at this connector: `<tid>#<oid>`, both canonical
/// lower case. Not the token's `sub`, which is pairwise per client ID (research
/// R4).
let microsoftSubject (tid: Guid) (oid: Guid) : string = $"{canonical tid}#{canonical oid}"

/// The inverse of `microsoftSubject`: None unless both halves are canonical GUIDs.
let parseMicrosoftSubject (subject: string | null) : (Guid * Guid) option =
    match subject with
    | null -> None
    | s ->
        match s.Split('#') with
        | [| t; o |] ->
            match tryCanonicalGuid t, tryCanonicalGuid o with
            | Some tid, Some oid -> Some(tid, oid)
            | _ -> None
        | _ -> None
