/// Serves an app's logo and the accent stylesheet its sign-in page links
/// (spec 006, research R4 and R7).
///
/// No authentication, no antiforgery, no rate limit, like Handlers/Assets.fs:
/// the sign-in page needs them before anyone is signed in.
module Umbral.Handlers.Branding
