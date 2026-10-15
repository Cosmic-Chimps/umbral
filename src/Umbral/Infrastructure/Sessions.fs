/// The end user's session with Umbral itself.
///
/// Distinct from the operator's admin session, and deliberately so: one is a
/// person signing in to an app through Umbral, the other is the one person who
/// configures it. They share no cookie and no scheme, so a bug in either cannot
/// grant the other.
///
/// This session is what makes single sign-on work. Someone who signs in to the
/// first app is not asked to choose a provider again at the second, because the
/// authorize handler finds this cookie and skips straight to issuing a code.
module Umbral.Infrastructure.Sessions

open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http

[<Literal>]
let Scheme = "Umbral.Session"

[<Literal>]
let CookieName = "umbral_session"

/// The provider KIND that authenticated this session (`discord`), carried so
/// the `idp` claim can tell an app "signed in with Discord" without a second
/// lookup. The kind and not the slug: with two Discord connectors, the slug
/// would hand an app a new value for the same person (research.md R3).
[<Literal>]
let IdpClaim = "idp"

/// The SLUG of the connector that established this session, which the
/// authorize endpoint checks against what the requesting app offers (FR-008).
/// Never passed on to apps. Sessions from before feature 003 do not have it.
[<Literal>]
let ConnectorClaim = "connector"

/// The domain of the VERIFIED email this session was admitted on, normalised
/// (`EmailDomains.domainOf`). Absent when the sign-in had no verified email, and
/// on a session from before specs/006. Only the domain, never the address: it is
/// all a later re-check needs, so no address has to sit in a cookie. Never passed
/// on to apps.
[<Literal>]
let EmailDomainClaim = "email_domain"

let configure (options: CookieAuthenticationOptions) =
    options.Cookie.Name <- CookieName
    options.Cookie.HttpOnly <- true
    options.Cookie.IsEssential <- true

    // Lax, not Strict: the browser arrives here on a top-level redirect back
    // from the provider, and a Strict cookie is withheld on exactly that
    // navigation. Not None either, which some browsers drop over plain http
    // and which would break local development.
    options.Cookie.SameSite <- SameSiteMode.Lax

    // Secure when the request is https, so one build works behind a TLS proxy
    // and on http://localhost without a second configuration knob.
    options.Cookie.SecurePolicy <- CookieSecurePolicy.SameAsRequest

    options.SlidingExpiration <- true
    options.ExpireTimeSpan <- System.TimeSpan.FromDays 14.0

    // There is no login page to redirect to at this scheme's level: the
    // authorize handler decides where an unauthenticated visitor goes, because
    // only it knows which authorization request is being resumed.
    options.LoginPath <- PathString "/signin"
    options.LogoutPath <- PathString "/connect/logout"
    options.AccessDeniedPath <- PathString "/signin"

/// Establish a session for a user who has just come back from a provider,
/// through the connector `slug` of the given `kind`. `emailDomain` is the domain
/// of the verified email that was checked at this sign-in, if there was one.
let signIn (ctx: HttpContext) (userId: string) (kind: string) (slug: string) (emailDomain: string option) : Task =
    let identity = ClaimsIdentity(Scheme)
    identity.AddClaim(Claim(ClaimTypes.NameIdentifier, userId))
    identity.AddClaim(Claim(IdpClaim, kind))
    identity.AddClaim(Claim(ConnectorClaim, slug))

    match emailDomain with
    | Some domain -> identity.AddClaim(Claim(EmailDomainClaim, domain))
    | None -> ()

    ctx.SignInAsync(Scheme, ClaimsPrincipal identity)

let signOut (ctx: HttpContext) : Task = ctx.SignOutAsync Scheme

/// The subject of the current session, if there is one. Note this says only
/// "a session exists for this id" — whether that user still exists and is
/// enabled is a database question, asked separately on every authorization.
let currentSubject (principal: ClaimsPrincipal) =
    match principal.FindFirst ClaimTypes.NameIdentifier with
    | null -> None
    | claim -> Some claim.Value

let currentIdp (principal: ClaimsPrincipal) =
    match principal.FindFirst IdpClaim with
    | null -> None
    | claim -> Some claim.Value

/// The slug of the connector that established the session. A session from
/// before the upgrade has no `connector` claim, and its `idp` holds the slug:
/// every slug that could exist then equalled its kind, so that one value serves
/// as both.
let currentConnector (principal: ClaimsPrincipal) =
    match principal.FindFirst ConnectorClaim with
    | null -> currentIdp principal
    | claim -> Some claim.Value

/// The domain of the verified email the session was admitted on. None means the
/// sign-in had no verified email, or the session is from before this feature:
/// either way a rule that applies cannot admit it (specs/006 research R6).
let currentEmailDomain (principal: ClaimsPrincipal) =
    match principal.FindFirst EmailDomainClaim with
    | null -> None
    | claim -> Some claim.Value
