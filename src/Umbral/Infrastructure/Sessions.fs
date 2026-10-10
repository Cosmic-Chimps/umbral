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

/// The connector slug that authenticated this session, carried so the `idp`
/// claim can tell an app "signed in with Discord" without a second lookup.
[<Literal>]
let IdpClaim = "idp"

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

/// Establish a session for a user who has just come back from a provider.
let signIn (ctx: HttpContext) (userId: string) (idp: string) : Task =
    let identity = ClaimsIdentity(Scheme)
    identity.AddClaim(Claim(ClaimTypes.NameIdentifier, userId))
    identity.AddClaim(Claim(IdpClaim, idp))
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
