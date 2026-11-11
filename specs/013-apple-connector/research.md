# Research: Sign in with Apple

Apple's documentation was read on 2026-11-11. Each rule below names the page it
comes from; the pages are on `developer.apple.com`.

## R1. What Apple's documents say (verified)

| Fact | Source |
| --- | --- |
| Discovery at `https://appleid.apple.com/.well-known/openid-configuration`: issuer `https://appleid.apple.com`, `authorization_endpoint` `/auth/authorize`, `token_endpoint` `/auth/token`, `jwks_uri` `/auth/keys`, `response_types_supported` `code`, `response_modes_supported` `query`, `fragment`, `form_post`, `id_token_signing_alg_values_supported` `RS256`, `token_endpoint_auth_methods_supported` `client_secret_post`, scopes `openid`, `email`, `name`. No userinfo endpoint, no `code_challenge_methods_supported`. | the document itself, fetched |
| Authorize request: `client_id`, `redirect_uri` ("must include a domain name and can't be an IP address or `localhost`"), `response_type` (`code`), `scope` ("Valid values are `name` and `email`"), `response_mode` ("If you requested any scopes, the value must be `form_post`"), `state`, `nonce`. | "Incorporating Sign in with Apple into other platforms" |
| The form post carries `code` ("single-use … valid for five minutes"), `state`, and `user`: `{ "name": { "firstName": string, "lastName": string }, "email": string }`. On Cancel: `error=user_cancelled_authorize` and `state`. | same page |
| The name and email are shared "the first time the user logs in using Sign in with Apple"; later the identity token has the email "but … doesn't include other information about the user, such as their name". | "Authenticating users with Sign in with Apple" |
| Token request: `POST https://appleid.apple.com/auth/token`, form fields `client_id`, `client_secret`, `code`, `grant_type=authorization_code`, `redirect_uri` ("must use the HTTPS protocol, include a domain name, and can't contain an IP address or localhost"). | "Generate and validate tokens" |
| Client secret: a JWT, header `alg` `ES256` and `kid` (the 10-character key identifier); claims `iss` (the 10-character Team ID), `iat`, `exp` ("an error to request an expiration time more than `15777000` seconds (six months) in the future"), `aud` `https://appleid.apple.com`, `sub` (the App ID or Services ID used as `client_id`). Signed with ECDSA, P-256, SHA-256. | "Creating a client secret" |
| Identity token claims: `iss` `https://appleid.apple.com`; `sub` unique and stable per developer team; `aud` the `client_id`; `exp`; `nonce`; `email` (real or relay address, may be empty for Work & School users); `email_verified` and `is_private_email`: "A string or Boolean value … either a string (\"true\" or \"false\") or a Boolean (true or false)". | "id_token" (Sign in with Apple JS) |
| Verification: signature with Apple's public key, `nonce`, `iss`, `aud`, `exp`. | "Verifying a user" |
| A Services ID is configured with domains and return URLs ("each registered URL must be absolute and must include the scheme, host, and path"); up to 10 website URLs for an individual, 100 for an organization; it is associated with a primary App ID that has Sign in with Apple enabled. | "Configuring your environment for Sign in with Apple", "Configure Sign in with Apple for the web" |
| The key: created under Keys by an Account Holder or Admin, downloaded once as a `.p8` text file ("you won't be able to download it again"); two keys at most per primary App ID. | "Create a private key to access a service", "Create a Sign in with Apple private key" |
| Relay addresses: to send mail to them, an app's owner registers its outbound email sources for the Private Email Relay Service. | "Configuring your environment…" |
| Button: custom buttons may only be titled "Sign in with Apple", "Sign up with Apple" or "Continue with Apple"; logo and title both black or both white on a white or black background; only Apple's logo artwork; no smaller than other sign-in buttons; at least 140 by 30 pt. | Human Interface Guidelines, "Sign in with Apple" |
| Guideline 4.8 as quoted in the spec's Background. | App Review Guidelines |

Not found in so many words: a statement that Sign in with Apple needs a paid
membership. Certificates, Identifiers & Profiles, where all of it is configured,
is part of the Apple Developer Program; the walkthrough says "membership of the
Apple Developer Program" without a price.

## R2. The correlation across a cross-site POST: a second cookie

Apple's answer is a POST from `appleid.apple.com` to Umbral. A `SameSite=Lax`
cookie is not sent with it, so today's `umbral_connect` cookie would be missing
and every Apple sign-in would end in `no_correlation_cookie`.

Two ways were on the table.

**A value in `state`, protected by the data-protection keys, and no cookie.**
`state` then proves that Umbral issued the value. It does not prove that the
browser delivering the answer is the one that started the sign-in. An attacker
starts a sign-in in their own browser, signs in at Apple as themselves, stops
before the POST reaches Umbral, and makes the victim's browser send it (a page
with an auto-submitting form is enough). The victim is then signed in to the
app as the attacker (login CSRF), and whatever they save lands in the
attacker's account. The cookie is what binds an answer to a browser; without
one there is nothing to bind to.

**A cookie that is sent on a cross-site POST: `SameSite=None; Secure`.** This is
what ASP.NET Core's own OpenID Connect handler does for the same reason. Chosen.

- Name `__Secure-umbral_connect_post`, path `/connect`, `HttpOnly`, `Secure`
  always (browsers drop a `SameSite=None` cookie without it; Apple accepts only
  https return URLs anyway). The `__Secure-` prefix costs nothing because the
  cookie is always `Secure`, and a browser then refuses to let a plain-http
  response plant or overwrite it, so a man in the middle on http cannot give the
  victim a correlation the attacker minted for their own sign-in. `__Host-`
  would also stop sibling subdomains but requires `Path=/`, and this cookie is
  limited to `/connect`. The `Lax` cookie cannot take a prefix because it is used
  over plain http in development. Ten minutes, encrypted and authenticated under
  its own data-protection purpose `Umbral.Connect.FormPost.v1`.
- Minted only when the provider's endpoints say it answers by form post. Every
  other connector keeps the `Lax` cookie unchanged: `None` is not sent over
  plain http, and the laptop trial runs on http.
- The two cookies have different names and different protector purposes, so a
  correlation minted for one mode cannot be redeemed in the other, by
  construction and not by a flag inside the payload.

`OAuthRoundTrip.complete` keeps its order: read the cookie, delete it, then
compare. The single-use table of redeemed states is shared by both modes.

## R3. The POST without an antiforgery token

Every other state-changing route of Umbral refuses a request with no
antiforgery token. This one cannot have one: the form is rendered by Apple, and
an antiforgery token is a value a page of Umbral's writes into its own form.
What takes its place:

1. **`state` and the cookie.** The POST is accepted only if the browser holds
   the form-post correlation cookie and the posted `state` equals the one inside
   it. The cookie is `HttpOnly` and encrypted; `state` is 256 random bits that
   left Umbral once, in the redirect to Apple, in this browser. A page on
   another site can make the browser send the cookie (that is what `None`
   means) but cannot read it and cannot know `state`, so its POST is refused.
   This is the antiforgery token's own construction (a secret in a cookie, a
   matching value in the request), with the request value travelling through
   Apple.
2. **What a forged POST can do.** Nothing but end one sign-in attempt: the
   cookie is deleted before anything is compared, so a forged POST spends the
   victim's correlation and their real answer then fails. They start again. The
   `Lax` flow has the same property through a forged top-level navigation. No
   session, user or link is written on any refused path.
3. **What the handler reads.** `state`, `code`, `error`, `user`. It does not read
   the session, and a session cookie (`Lax`) is not sent on this request anyway.
   The session cookie it sets on success is kept by the browser (SameSite limits
   sending, not setting), and the redirect that follows is a top-level GET, on
   which `Lax` cookies are sent.
4. **The `user` field** is the one thing in the POST that nothing signs. It is
   read only after the `id_token` validated, only for the display name, and
   never for the email. Whoever can deliver a valid `state` is the person signing
   in, so the most a forged `user` does is change that person's own display name.
5. **Limits.** The sign-in rate limit of the GET callback; a 64 KB body.

`CsrfTests` already leaves `/connect/*` outside the antiforgery walk, for the
reason its module comment gives (the protocol's own machinery protects them);
the comment gets a line naming this route.

## R4. Signing the client secret

`JsonWebTokenHandler.CreateToken` with a `SecurityTokenDescriptor`, an
`ECDsaSecurityKey` (its `KeyId` becomes `kid`) and `SecurityAlgorithms.EcdsaSha256`.
The library is already referenced (`Microsoft.IdentityModel.Protocols.OpenIdConnect`
8.19.2 brings `Microsoft.IdentityModel.JsonWebTokens`). No new package.

- **Exactly the documented claims.** `SetDefaultTimesOnTokenCreation = false`,
  with `IssuedAt` and `Expires` set by hand, so the token has `iss`, `sub`,
  `aud`, `iat`, `exp` and no `nbf`.
- **Five minutes.** Apple allows six months, which is why other tools ask the
  operator to generate a token and renew it twice a year. Umbral holds the key,
  so it signs one per token request and the token is useless a few minutes
  later. Nothing is cached: one ES256 signature per sign-in is not a cost.
- **The key object is not cached either.** The PEM is imported into an `ECDsa`
  for the one signature and disposed. The library keeps signature providers in
  a cache keyed by key id by default, which would hold on to a disposed key and
  fail the second sign-in with `ObjectDisposedException`; the key gets its own
  `CryptoProviderFactory` with `CacheSignatureProviders = false`.
- **The clock** is Umbral's `TimeProvider`, like token lifetimes in the OIDC
  adapter, so a test can pin `iat` and `exp`.
- **Reading the key.** `ECDsa.ImportFromPem` accepts the PKCS#8 form Apple
  issues (`-----BEGIN PRIVATE KEY-----`). Accepted only when the import succeeds
  and the curve is P-256 (`KeySize = 256`). Line endings are normalised and the
  text trimmed before it is stored, so a paste from Windows signs the same.
- **Never in a message.** A failed import yields a fixed word
  (`apple_key_unusable`); the exception's text is not used.

## R5. PKCE

Apple's pages do not mention `code_challenge`, and the discovery document has
no `code_challenge_methods_supported`. The OIDC adapter's rule for that silence
(specs/002 R5) is to send the challenge and the verifier: a server that does
not know a parameter ignores it (RFC 6749 §3.1), and widely used clients send
PKCE to Apple. Umbral does the same and relies on it for nothing: `state`, the
cookie and `nonce` are the checks. If Apple ever refused the parameter, the
change is the one flag (`SupportsPkce`). This is the one item of this spec that
cannot be confirmed without real credentials; the first real sign-in confirms
it.

## R6. Reuse of the OIDC adapter

Apple is an OpenID Connect provider with a fixed issuer and three differences
(the signed client secret, the form post, the name outside the token). So the
Apple adapter uses the OIDC adapter's own functions, made public in `Oidc.fs`:

- `OidcDiscovery.Check "https://appleid.apple.com"` for the endpoints and keys,
  with its validation, caching and throttled refresh;
- the token exchange (`client_secret_post`, because Apple's document lists only
  that; the secret passed in is the signed JWT);
- `id_token` validation, extracted from `OidcAdapter.Validate` into a function
  that returns the validated payload. Behaviour for `oidc` connectors is
  unchanged and stays pinned by `OidcProtocolTests`.

What stays Apple's own: the mapping from the payload to an identity. The OIDC
adapter accepts only the JSON literal `true` for `email_verified` (specs/002
FR-018), because an arbitrary provider's string is not trusted. Apple documents
the string form, so the Apple adapter, and only it, also accepts exactly
`"true"`.

The subject stored is Apple's `sub` as it is (kind `apple`), not
`issuer#sub`: there is one issuer.

## R7. The name

`UserStore.resolve` already writes `display_name = COALESCE(@name, display_name)`
for a returning user and "<Kind> user" for a new one without a name. So the
adapter returns `DisplayName = None` when the answer has no usable `user` field,
and a stored name is never cleared. No change to the store.

## R8. The button and the logo

The designer's decision (2026-11-11): the Apple button follows Apple's rules for
a custom button. Apple's logo artwork must be the file from Apple Design
Resources; it cannot be drawn, and whether it may be bundled in an MIT
repository is the maintainer's to confirm. This change ships the button with the
title alone and a marked place for the inline SVG. A follow-up task carries the
asset. Until then the walkthrough says the connector is not ready for an App
Store submission.

## R9. Storage

Two nullable columns on `connectors` (`apple_team_id`, `apple_key_id`), schema
version 10. A separate table was considered and dropped: two values that belong
to exactly one row are columns, as `issuer_url` and `passkey_enrolment` are.
