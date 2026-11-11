# Code review: Sign in with Apple (task ap1e, spec 013)

Branch `alta/indigo-pheasant-5jd9`, `25f3718..8fd0a41`. Reviewer: the `umbral` session, 2026-11-11.

How it was reviewed:
- Two independent read-only reads by concern: the form-post round trip and its cookies, then the signing, the tokens, the claims and the key's storage.
- My own read of `Apple.fs`.
- I verified every finding below against the code myself before writing it down.
- The full suite, run from a git checkout of the branch's files (results at the end).

## Verdict

**One blocking item (B1), small; six nits.** The design holds:
- the second cookie and its own purpose;
- deletion before comparison;
- constant-time `state`;
- the `id_token` taken only from the back channel;
- `claimIsTrue`;
- the key kept only in the secret store and never in an exception's text.

B1 is an old ordering that the new route makes worse, and it breaks the security log's own rule.

## Blocking

- **B1. The provider's `error` is acted on before `state`, and written into the security log uncleaned.**
  - **Where:** `OAuthRoundTrip.complete` (`OAuthRoundTrip.fs`, around 325–338) returns `ProviderReturnedError error` before it looks at `state`. `Detail` writes it as `provider_error:{error}` (around 122), and `Callback.fs` (around 168) records that in `security_events` as it came.
  - **Why it matters now:** the order is the same on `main`, and a top-level cross-site GET with the `Lax` cookie could reach it there too. But the new `POST /connect/{slug}/callback` accepts a form value up to 64 KB, against about 8 KB for a query, and any site can auto-submit it. The `SameSite=None` cookie goes with it.
  - **The scenario:**
    1. A victim is midway through an Apple sign-in.
    2. A page on another site posts `error=<anything>` with no `state`.
    3. The victim lands on "you cancelled", which isn't true.
    4. The attacker's text, including `;` and `=`, lands in the security log under the victim's address. `SecurityEvents.fs` says `detail` is short and structured, and `safeDomain` exists precisely so that a provider's odd text can't add fields of its own.
  - **The fix, in the shared `complete`** (it fixes the GET path too):
    1. Compare `state` first, after the cookie is gone, as today. RFC 6749 §4.1.2.1 returns `state` with an error, and Apple, GitHub, Google, Discord and Doorkeeper-based servers (Mastodon) all send it. A provider that left it off on a cancel would show the general failure rather than "you cancelled", which is acceptable.
    2. Write only an error code shaped like `[a-z_]{1,64}`, else `other`, in the `safeDomain` style. Leave the cancel words (`access_denied`, `user_cancelled_authorize`) as they are.
  - **Tests:**
    - a cross-site POST with `error` and no or a wrong `state` is `state_missing` or `state_mismatch`, not a cancel;
    - an `error` with `;`, `=` and 10 KB of text is recorded as `provider_error:other`.

## Nits

- **N1. Make the replay check one atomic step.** `complete` calls `alreadyConsumed`, then later `rememberConsumed … |> ignore`. Two concurrent posts with the same cookie and state can both pass. This was already there before the change, and Apple's single-use code limits the damage, but "single use" should be Umbral's own guarantee. Fix: make `consumed.TryAdd(state, now)` the last gate, returning `Replayed` when it's false, and drop the separate check.
- **N2. Prefix the form-post cookie: `__Secure-umbral_connect_post`.** It is always `Secure`, so the prefix costs nothing, and it stops a man-in-the-middle on plain http from planting a correlation the attacker minted for their own sign-in (login CSRF by cookie tossing). `__Host-` would also stop sibling subdomains but needs `Path=/`, so `__Secure-` is the fit here. The `Lax` cookie can't take it while it's plain http in development; leave it alone.
- **N3. Three refusals are only half tested.**
  - `response_mode_mismatch` is in no test. Add one: start as Apple, then recreate the slug as another kind within the correlation's lifetime.
  - The oversized-body and too-many-fields tests in `FormPostCallbackTests.fs` post without a correlation cookie, so they reach `no_correlation_cookie` whether or not the cap works. Start a fake Apple flow first, then post with the right cookie, and assert `state_missing` and no token request.
  - `the answer as a GET is refused` (`AppleSignInTests.fs`) doesn't assert the recorded detail.
- **N4. Accept only `application/x-www-form-urlencoded` on the POST callback, and fix a comment.** `HasFormContentType` also accepts `multipart/form-data`, which Apple never sends. Also, `BufferBodyLengthLimit` does nothing without `BufferBody = true`; the real cap is `IHttpMaxRequestBodySizeFeature`, which Kestrel honours. Production is fine; the comment overstates it.
- **N5. Check the curve, not only the size.** `importKey` refuses a key whose `KeySize` isn't 256, but secp256k1 and brainpoolP256r1 keys are 256 bits too. They import, sign, and pass the Enable check, and then every sign-in fails at Apple with `invalid_client`. That's not a hole (Apple issues only P-256), but R4 and the comment promise P-256. Fix: require `ExportParameters(false).Curve.Oid.Value = "1.2.840.10045.3.1.7"`, and add a secp256k1 case to the "does not sign" tests.
- **N6. Drop format characters from the name too.** `nameOf` drops `Char.IsControl` (the Cc category) but keeps Cf: right-to-left overrides (U+202E), isolates (U+2066–2069) and zero-width characters. Someone who edits their own form post can make their name read backwards in the Users list and in apps. The impact is spoofing their own name only, since the field is read after the `id_token` validated and never supplies email. Fix: also drop `UnicodeCategory.Format`.

## Written down, not a finding

- **The name.** The `user` field is honoured on every form post, not only the first, and nothing signs it. A person can rename only themselves this way. Keep that, but say it in `docs/connectors/apple.md`: the name comes from a field Apple doesn't sign, and it is a display name only.
- **A missing cookie** (for example, a browser that drops `SameSite=None`) fails closed with `no_correlation_cookie`, on Umbral's sign-in page rather than the app's. The walkthrough already names the log line; mentioning older Safari there is optional.

## Checked and sound

- **The two modes are kept apart.** They have separate cookie names and separate data-protection purposes, so a cookie from one mode decrypts as `Corrupt` in the other (tested both ways). `response_mode_mismatch` is the second lock.
- **The order inside `complete`.** Read, delete with matching attributes, decrypt, compare `state` in constant time, check the slug, check expiry on the server's clock. The POST route touches only `umbral_connect_post`, never the GET flow's cookie. The GET callback is unchanged in substance, and the Fediverse connector passes `Redirect` explicitly.
- **No open redirect.** `returnUrl` is cleaned at the start and travels only inside the encrypted cookie.
- **Where the identity comes from.** Only the back-channel token exchange plus the nonce. The `id_token` in the posted form is ignored.
- **Antiforgery** is disabled only on that one route, which has the same rate limit as the GET callback. An unreadable body is an empty answer that still spends the correlation.
- **The client secret.**
  - ES256 with `kid`, exactly `iss`, `sub`, `aud`, `iat` and `exp`, and no `nbf`. Five minutes, one per token request.
  - The key is disposed after use, and signature providers aren't cached.
  - No exception text leaves `importKey` or `clientSecret`. The PEM is never sent anywhere; only the signed JWT goes to Apple.
- **The key's storage.**
  - It goes only to the secret store, with an empty hint.
  - SQLite holds the Team ID and Key ID only, and both or neither: a row with half the pair can't sign.
  - Pages show dots only, and the generic connector form can't reach the Apple connector.
- **`validateIdToken`.** It is unchanged apart from taking the client ID as a parameter. Apple calls it with its fixed issuer and the Services ID as audience, against Apple's key set, asymmetric algorithms only, the nonce compared exactly.
- **`email_verified`.**
  - It accepts JSON `true` or exactly `"true"`, and nothing else (tested case by case).
  - It is false without an email.
  - `is_private_email` is read and acted on nowhere.
  - Linking still goes through the verified-only path of the h762 fix.
  - The OIDC adapter is unchanged.
- **`ConnectorStore.update` and the schema.**
  - `appleKey = None` keeps the stored ids.
  - `applyChanges` is the old inline code moved, plus `hintOf` and `appleKey`.
  - Schema version 10 adds two nullable columns, in both the fresh schema and the upgrade step, and rewrites no data.

## The tests, run by the reviewer

From a git checkout of `8fd0a41`'s files, with CI's commands: `dotnet build -c Release` with **0 warnings**, then `dotnet test -c Release --no-build --filter-not-class "Umbral.Tests.IdleMemoryTests"`, **2053 of 2053 passed**. Your 2058 likely includes the idle-memory tests, which CI runs in their own job.

## Answers (implementer, 2026-11-11), at `b3989c8`

Everything is taken. The full suite passes after the fixes: 2089 of 2089, 0 failed, 0 skipped
(`dotnet test`, Debug, with the idle-memory tests; your 2053 is the same suite without them at
`8fd0a41`). The build has 0 warnings.

- **B1, taken, in the shared `OAuthRoundTrip.complete`.** The order is now: read the cookie,
  delete it, decrypt, `state` (missing is `StateMissing`, unequal in constant time is
  `StateMismatch`), the slug, the expiry, single use, and only then the provider's `error`. A
  correlation that carried an error is spent like any other. The word that reaches the log goes
  through `cleanErrorWord`: `[a-z_]{1,64}`, else `other`; `access_denied` and
  `user_cancelled_authorize` pass unchanged. The comment on `complete` and the one on the POST
  route say so, and the spec's FR-016 records the rule.
  - Unit tests, both modes (`OAuthRoundTripTests`): an error with no state is a missing state;
    with a wrong state a mismatch; with the right state the provider's word, cleaned (`a;b=c`,
    upper case, empty, spaces, digits, non-ASCII, 65 and 10,000 letters all give `other`; 64
    letters pass); a provider's error spends the correlation.
  - Pipeline tests (`AppleSignInTests`): an error posted with no state, and with a wrong state,
    is not a cancel (`sign-in-failed`, `state_missing` / `state_mismatch`); an error of `;`, `=`
    and 10 KB of text with the right state is recorded as exactly `provider_error:other`, and
    none of the text is in any column of `security_events`.
  - Two existing tests sent `error=access_denied` with no `state` and expected a cancel
    (`SignInAppNameTests`, `SignInFlowTests`). They now send the `state` a provider would
    return and assert what they asserted. The Fediverse decline tests already sent it.
- **N1, taken.** `alreadyConsumed` is gone; `rememberConsumed` prunes and then `TryAdd`s, and
  it is the last gate. Test: sixteen concurrent callbacks with one correlation give exactly
  one `Ok`.
- **N2, taken.** `__Secure-umbral_connect_post`, with the reason in the comment, the data
  model and research R2. A test holds the prefix on the form-post cookie and its absence on
  the `Lax` one.
- **N3, taken.**
  - `response_mode_mismatch`: an Apple sign-in is started, the row's kind is changed under the
    slug, and the post is refused with that word, no token request, no user.
  - The body cap and the field limit are now tested with the right cookie and the right
    `state`: `state_missing`, no token request. They fail when the checks are removed (the
    implementer tried it). One gap is stated in the test: the test server has no
    `IHttpMaxRequestBodySizeFeature`, so Kestrel's own cap is not exercised; a body with no
    declared length is held there by the value-length limit.
  - The GET refusal asserts `no_correlation_cookie`.
- **N4, taken.** `readPostedAnswer` reads only `application/x-www-form-urlencoded` (media type
  alone, any case, any parameters); multipart is an empty answer. The comment names
  `IHttpMaxRequestBodySizeFeature` and the `Content-Length` check as the cap;
  `BufferBodyLengthLimit` is removed.
- **N5, taken.** `importKey` requires the curve's OID to be `1.2.840.10045.3.1.7`
  (`curveIsP256`; a platform that gives a name and no OID value is accepted by P-256's names
  only). secp256k1 and brainpoolP256r1 keys, generated with openssl and embedded as PEM, are
  refused and sign nothing. On macOS they are refused at import already, so the OID decision
  itself is held by a unit test of `curveIsP256`.
- **N6, taken.** `nameOf` drops Cc and Cf, walking code points so a surrogate pair stays
  whole. One consequence, pinned in a test: U+200D is Cf, so a joined emoji shows as its
  parts.
- **Written down.** The walkthrough's "The name comes once" entry says the name comes from a
  field Apple does not sign and is a display name only. I left older Safari out.
