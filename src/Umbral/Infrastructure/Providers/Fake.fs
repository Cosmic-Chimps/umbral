/// A provider that lives inside Umbral, for tests and demos.
///
/// It makes the whole sign-in path exercisable with no network and no
/// credentials: the real correlation cookie, the real `state` comparison, the
/// real linking rule, the real token issuance. Only the hop to a third party is
/// replaced.
///
/// **This is a total authentication bypass if it ever ships enabled.** A fake
/// identity provider will say anyone is anyone. So it is gated twice — the
/// environment must be Development AND the operator must have asked for it —
/// and Umbral refuses to start if a connector of this kind exists without both.
/// See `FakeGate` below; the refusal is in Program.initialize.
module Umbral.Infrastructure.Providers.Fake

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.DataProtection
open Microsoft.Extensions.Configuration
open Umbral.Domains.Connectors
open Umbral.Infrastructure.ProviderAdapters

[<Literal>]
let AuthorizePath = "/dev/fake-provider/authorize"

[<Literal>]
let private Purpose = "Umbral.FakeProvider.v1"

[<Literal>]
let private Separator = '\u001f'

/// Both conditions, read together in one place so neither can be checked
/// without the other.
///
/// Phrased as "is Development" rather than "is not Production" deliberately:
/// ASPNETCORE_ENVIRONMENT is often unset, and the negative form would read as
/// "allowed by default" — fail-open, in the one place that must never be.
type FakeGate(config: IConfiguration) =
    member _.IsAllowed =
        config["ASPNETCORE_ENVIRONMENT"] = "Development"
        && config["UMBRAL:ALLOW_FAKE_CONNECTOR"] = "true"

    member this.Explain =
        if this.IsAllowed then
            "The fake connector is enabled. This instance will authenticate anyone as whoever they claim to be."
        else
            "A connector of kind 'fake' is configured, but the fake connector is not enabled. "
            + "It authenticates anyone as whoever they claim to be, so it requires BOTH "
            + "ASPNETCORE_ENVIRONMENT=Development and UMBRAL__ALLOW_FAKE_CONNECTOR=true. "
            + "Delete the connector, or set both if this really is a development instance."

/// The "authorization code" the fake provider hands back is the profile itself,
/// encrypted. There is no upstream to ask, and inventing a token endpoint to
/// call would test Umbral's ability to talk to itself rather than anything real.
type FakeAdapter(protectorProvider: IDataProtectionProvider, gate: FakeGate) =
    let protector = protectorProvider.CreateProtector Purpose

    member _.IssueCode(subject: string, email: string, emailVerified: bool, displayName: string) =
        String.Join(Separator, [| subject; email; string emailVerified; displayName |])
        |> protector.Protect

    interface IProviderAdapter with
        member _.Kind = ProviderKind.Fake

        member _.Resolve connector =
            Task.FromResult(Ok(endpointsFor connector.Kind))

        member _.BuildAuthorizeUrl request =
            // Points back at Umbral itself. Everything else — state, the
            // correlation cookie, the callback route — is the real path.
            //
            // Absolute, built from the redirect URI's own origin: a relative
            // path has nothing to resolve against and `Uri` turns it into a
            // file:// URL rather than failing, which surfaces much later as a
            // redirect to nowhere.
            let origin = request.RedirectUri.GetLeftPart UriPartial.Authority

            buildUrl
                (origin + AuthorizePath)
                [ "redirect_uri", request.RedirectUri.ToString()
                  "state", request.State
                  "slug", request.Connector.Slug ]

        member _.ExchangeAndFetch request =
            task {
                // Checked again at the moment of use, not only at startup: a
                // connector added to the database while the process runs must
                // not become a live bypass.
                if not gate.IsAllowed then
                    return Error(ProviderRejected "the fake connector is not enabled")
                else
                    try
                        match (protector.Unprotect request.Code).Split Separator with
                        | [| subject; email; verified; displayName |] ->
                            return
                                Ok
                                    { Subject = subject
                                      Email = (if email = "" then None else Some email)
                                      EmailVerified = verified = "True"
                                      DisplayName = (if displayName = "" then None else Some displayName)
                                      AvatarUrl = None }
                        | _ -> return Error(MalformedResponse "the fake code did not decode")
                    with _ ->
                        return Error(MalformedResponse "the fake code could not be decrypted")
            }
