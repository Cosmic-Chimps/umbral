/// Which email domains may sign in (specs/006-allowed-email-domains).
///
/// Pure, like `Linking`, and for the same reason: this is the rule that keeps a
/// stranger out, so it is a total function over values that every caller (the
/// callback, the authorize and token endpoints, the Google hint, the admin
/// screens) shares, and no caller can disagree with another about it.
///
/// A domain here is always the normalised ASCII form: lower case, the IDN
/// (`münchen.de`) as `xn--mnchen-3ya.de`. Matching is exact equality, so a
/// subdomain is a different domain and has to be listed on its own.
module Umbral.Domains.EmailDomains

open System
open System.Globalization

/// Why one entry the operator typed was refused. The entry is as typed.
type ParseError =
    /// `*` anywhere in it. Its own case because it is the likely mistake and
    /// the way out is different: list each domain.
    | Wildcard of entry: string
    /// Not a domain name: a single label, an IP address, a bad character.
    | NotADomain of entry: string

/// Where the list that applies comes from.
type Level =
    | Connector
    | Instance

/// What applies to one connector: nothing, or a non-empty list from one level.
type Rule =
    | NoRule
    | Rule of Level * string list

type Refusal =
    | NoEmail
    | EmailNotVerified
    /// The normalised domain, or None when the mapping refused it.
    | DomainNotAllowed of domain: string option

type Decision =
    | Admitted
    | Refused of Level * Refusal

let private idn = IdnMapping()

let private asciiOf (name: string) : string option =
    try
        Some((idn.GetAscii(name.ToLowerInvariant())).ToLowerInvariant())
    with :? ArgumentException ->
        None

let private isLabel (label: string) =
    label.Length >= 1
    && label.Length <= 63
    && label.[0] <> '-'
    && label.[label.Length - 1] <> '-'
    && label |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-')

/// At least two labels, each a valid label, and a top-level label that is not
/// all digits, so an IP address is not a domain.
let private isDomain (name: string) =
    let labels = name.Split '.'

    name.Length >= 1
    && name.Length <= 253
    && labels.Length >= 2
    && Array.forall isLabel labels
    && not (labels[labels.Length - 1] |> Seq.forall Char.IsDigit)

/// The ASCII, lower-case, validated form of a domain name, or None when it is
/// not one. Shared with Domains/Fediverse.fs so a server address and an email
/// domain follow one rule.
let normaliseDomain (name: string) : string option =
    match asciiOf name with
    | Some ascii when isDomain ascii -> Some ascii
    | _ -> None

let private normaliseEntry (entry: string) : Result<string, ParseError> =
    if entry.Contains '*' then
        Error(Wildcard entry)
    else
        let trimmed = entry.TrimStart('@').TrimEnd('.')

        match asciiOf trimmed with
        | Some name when isDomain name -> Ok name
        | _ -> Error(NotADomain entry)

/// What the operator typed, as a list of normalised domains. Commas, spaces,
/// tabs and new lines separate; a leading `@` and a trailing `.` are dropped;
/// duplicates go, keeping the first order. Empty input is `Ok []`. The first
/// entry that is not usable is the error, and nothing is kept.
let parse (input: string) : Result<string list, ParseError> =
    let entries =
        input.Split([| ','; ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray

    let folded =
        entries
        |> List.fold
            (fun state entry ->
                match state with
                | Error _ -> state
                | Ok names ->
                    match normaliseEntry entry with
                    | Ok name -> Ok(name :: names)
                    | Error error -> Error error)
            (Ok [])

    folded |> Result.map (List.rev >> List.distinct)

/// The stored form: normalised ASCII domains joined by one space. A space never
/// appears in a domain, so nothing is lost.
let serialise (domains: string list) : string = String.concat " " domains

/// The inverse of `serialise`. Reads what is stored, without judging it.
let deserialise (stored: string) : string list =
    stored.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

/// What the operator reads back: the Unicode form, joined by a comma and a space.
let display (domains: string list) : string =
    domains
    |> List.map (fun name ->
        try
            idn.GetUnicode name
        with :? ArgumentException ->
            name)
    |> String.concat ", "

/// The list that applies: the connector's when it has one, else the instance's
/// when it has one, else none. Neither list is merged with the other: a
/// connector's own list replaces the instance's for that connector.
let applying (connectorList: string list) (instanceList: string list) : Rule =
    match connectorList, instanceList with
    | _ :: _, _ -> Rule(Connector, connectorList)
    | [], _ :: _ -> Rule(Instance, instanceList)
    | [], [] -> NoRule

/// What the part after an address's last `@` came to.
type private DomainPart =
    | NoDomainPart
    | Unmappable
    | Mapped of string

let private domainPart (email: string option) : DomainPart =
    let text = email |> Option.map (fun e -> e.Trim()) |> Option.defaultValue ""

    match text.LastIndexOf '@' with
    | -1 -> NoDomainPart
    | at ->
        let after = text.Substring(at + 1).Trim().TrimEnd('.')

        if after = "" then
            NoDomainPart
        else
            match asciiOf after with
            | Some name -> Mapped name
            | None -> Unmappable

/// The normalised domain of an address: the part after the last `@`, one
/// trailing dot trimmed, in ASCII and lower case. None when there is no address,
/// no `@`, nothing after it, or a name the mapping refuses.
let domainOf (email: string option) : string option =
    match domainPart email with
    | Mapped name -> Some name
    | NoDomainPart
    | Unmappable -> None

/// Decide. The order of the refusals is fixed: no email, then not verified, then
/// the domain. `NoRule` admits everyone, so an instance with no list behaves as
/// it did before the feature.
///
/// `verified` is strictly what the provider asserted. An unverified address
/// proves nothing about its domain, so it is refused however it reads.
let check (rule: Rule) (email: string option) (verified: bool) : Decision =
    match rule with
    | NoRule -> Admitted
    | Rule(level, domains) ->
        match domainPart email with
        | NoDomainPart -> Refused(level, NoEmail)
        | _ when not verified -> Refused(level, EmailNotVerified)
        | Unmappable -> Refused(level, DomainNotAllowed None)
        | Mapped name ->
            if List.contains name domains then
                Admitted
            else
                Refused(level, DomainNotAllowed(Some name))

/// Decide again, later, from what a sign-in left behind. A session, an
/// authorization code and a refresh token carry only the normalised domain of
/// the verified email checked at sign-in (specs/006 R6, R7), never the address,
/// so there is nothing else to judge by. A domain present was a verified one;
/// absent means the sign-in had no verified email, or came from before this
/// feature, and a rule that applies cannot admit it.
let checkDomain (rule: Rule) (domain: string option) : Decision =
    match rule with
    | NoRule -> Admitted
    | Rule(level, domains) ->
        match domain with
        | None -> Refused(level, NoEmail)
        | Some name ->
            if List.contains name domains then
                Admitted
            else
                Refused(level, DomainNotAllowed(Some name))
