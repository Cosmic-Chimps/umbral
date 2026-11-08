import Foundation
@preconcurrency import AppAuth
import UIKit

/// Everything the sample does with AppAuth, in one place.
///
/// AppAuth does the security-relevant work: it opens the system's sign-in browser
/// (`ASWebAuthenticationSession`, never a `WKWebView`), sends a PKCE S256 challenge, a `state` and a
/// `nonce`, checks `state` when the browser comes back, and validates the id_token (issuer,
/// audience, expiry, nonce) during the code exchange. We leave all of that at its defaults.
/// AppAuth does not check the id_token's signature: it relies on TLS to the token endpoint.
@MainActor
@Observable
final class UmbralAuth {

    // One slash after the colon: Umbral matches redirect URIs exactly.
    static let redirectURL = URL(string: "com.cosmicchimps.umbral.sample:/callback")!
    static let postLogoutRedirectURL = URL(string: "com.cosmicchimps.umbral.sample:/signout-callback")!

    /// `true` opens a private browser session: no cookies shared with Safari, so Umbral's page every
    /// time and no "wants to use … to sign in" prompt, and no Umbral session left to end at sign-out.
    /// `false` shares Safari's cookies, so a person already signed in to Umbral is not asked again.
    static let prefersEphemeralSession = false

    private var authState: OIDAuthState?
    /// The browser session in flight (sign-in or sign-out). AppAuth needs it kept alive.
    private var session: (any OIDExternalUserAgentSession)?

    init() {
        authState = TokenStore.load()
    }

    // MARK: Claims

    /// The claims to show when signed in, nil when signed out. Signed in means: the stored state is
    /// authorized and its id_token has not expired. There is no refresh token (no `offline_access`),
    /// so an expired token ends the session locally and the next sign-in asks again.
    func claims() -> [(name: String, value: String)]? {
        // AppAuth already validated the id_token during the code exchange.
        guard let state = authState, state.isAuthorized,
              let raw = state.lastTokenResponse?.idToken,
              let idToken = OIDIDToken(idTokenString: raw)
        else { return nil }
        if idToken.expiresAt <= Date() {
            discard()
            return nil
        }
        var result = [(name: "sub", value: idToken.subject)]
        // name and idp are always there; email and email_verified only when Umbral sent them.
        for name in ["name", "idp", "email", "email_verified"] {
            if let value = idToken.claims[name] {
                result.append((name: name, value: Self.text(of: value)))
            }
        }
        return result
    }

    /// A JSON boolean arrives as an NSNumber: show it as true/false, not 1/0.
    private static func text(of value: Any) -> String {
        if let number = value as? NSNumber, CFGetTypeID(number) == CFBooleanGetTypeID() {
            return number.boolValue ? "true" : "false"
        }
        return "\(value)"
    }

    // MARK: Sign-in

    /// Discovers the endpoints from the issuer, opens the browser, exchanges the code and keeps the
    /// tokens. Calls `completion` with an error message, or nil when signed in.
    func signIn(completion: @escaping @MainActor (String?) -> Void) {
        let issuer: URL
        switch Self.checkedIssuer() {
        case .failure(let failure):
            completion(failure.message)
            return
        case .success(let url):
            issuer = url
        }
        // Endpoints come from /.well-known/openid-configuration only, never hard-coded.
        // AppAuth calls back on the main queue, but its types are not Sendable: hand them over as is.
        OIDAuthorizationService.discoverConfiguration(forIssuer: issuer) { [weak self] discovered, error in
            nonisolated(unsafe) let discovered = discovered
            MainActor.assumeIsolated {
                guard let self else { return }
                guard let configuration = discovered else {
                    completion(Self.describe("Could not reach Umbral", error))
                    return
                }
                guard let presenting = Self.presentingViewController() else {
                    completion("Sign-in failed: no window to present from")
                    return
                }
                // This initializer generates `state`, `nonce` and a PKCE S256 verifier. A public
                // client: no secret, and the token request carries no client authentication.
                let request = OIDAuthorizationRequest(
                    configuration: configuration,
                    clientId: UmbralSettings.UMBRAL_CLIENT_ID,
                    clientSecret: nil,
                    scopes: [OIDScopeOpenID, OIDScopeProfile, OIDScopeEmail],
                    redirectURL: Self.redirectURL,
                    responseType: OIDResponseTypeCode,
                    additionalParameters: nil)
                self.session = OIDAuthState.authState(
                    byPresenting: request,
                    presenting: presenting,
                    prefersEphemeralSession: Self.prefersEphemeralSession
                ) { [weak self] signedIn, error in
                    nonisolated(unsafe) let signedIn = signedIn
                    MainActor.assumeIsolated {
                        guard let self else { return }
                        self.session = nil
                        guard let state = signedIn else {
                            completion(Self.signInMessage(error))
                            return
                        }
                        self.authState = state
                        // Signed in either way; a failed write only means it will not survive a restart.
                        let status = TokenStore.save(state)
                        if status != errSecSuccess {
                            let message = "Signed in, but the Keychain did not keep the tokens (OSStatus \(status)): "
                                + "you will be signed out when the app closes."
                            print(message)
                            completion(message)
                            return
                        }
                        completion(nil)
                    }
                }
            }
        }
    }

    /// The fallback for a redirect that reaches the app as a URL instead of ending the browser session.
    func resume(with url: URL) {
        _ = try? session?.resumeExternalUserAgentFlow(url)
    }

    // MARK: Sign-out

    /// Signs out in the order the contract asks: end the app's own session first (tokens gone from
    /// memory and the Keychain), then send the person to Umbral's end-session endpoint. Either way
    /// this device is signed out as soon as this is called. `completion` gets an error message from
    /// the end-session step, or nil (including when the person cancelled).
    func signOut(completion: @escaping @MainActor (String?) -> Void) {
        let idToken = authState?.lastTokenResponse?.idToken
        let configuration = authState?.lastAuthorizationResponse.request.configuration
        discard()
        guard let idToken, let configuration, configuration.endSessionEndpoint != nil,
              let presenting = Self.presentingViewController(),
              let userAgent = OIDExternalUserAgentIOS(
                presenting: presenting, prefersEphemeralSession: Self.prefersEphemeralSession)
        else {
            completion(nil)
            return
        }
        let request = OIDEndSessionRequest(  // AppAuth adds `state`
            configuration: configuration,
            idTokenHint: idToken,
            postLogoutRedirectURL: Self.postLogoutRedirectURL,
            additionalParameters: nil)
        session = OIDAuthorizationService.present(request, externalUserAgent: userAgent) { [weak self] _, error in
            MainActor.assumeIsolated {
                self?.session = nil
                // Cancelled or failed: we are signed out anyway; only a real error is shown.
                completion(Self.isCancel(error) ? nil : error.map { Self.describe("Sign-out at Umbral failed", $0) })
            }
        }
    }

    private func discard() {
        authState = nil
        TokenStore.clear()
    }

    // MARK: Helpers

    struct IssuerError: Error { let message: String }

    /// https always; http only to localhost or 127.0.0.1 (a local Umbral in the Simulator).
    /// Umbral itself accepts plain http only on loopback. Checked before any network call.
    static func checkedIssuer() -> Result<URL, IssuerError> {
        guard let url = URL(string: UmbralSettings.UMBRAL_ISSUER),
              let scheme = url.scheme?.lowercased(), let host = url.host, !host.isEmpty
        else { return .failure(IssuerError(message: "UMBRAL_ISSUER is not a valid URL")) }
        switch scheme {
        case "https":
            return .success(url)
        case "http" where ["localhost", "127.0.0.1"].contains(host.lowercased()):
            return .success(url)
        case "http":
            return .failure(IssuerError(message: "Refusing http:// for \(host): https only (http for localhost)"))
        default:
            return .failure(IssuerError(message: "UMBRAL_ISSUER must start with https://"))
        }
    }

    private static func isCancel(_ error: (any Error)?) -> Bool {
        guard let error = error as NSError? else { return false }
        return error.domain == OIDGeneralErrorDomain
            && error.code == OIDErrorCode.userCanceledAuthorizationFlow.rawValue
    }

    private static func signInMessage(_ error: (any Error)?) -> String {
        isCancel(error) ? "Sign-in cancelled" : describe("Sign-in failed", error)
    }

    private static func describe(_ what: String, _ error: (any Error)?) -> String {
        let detail = (error as NSError?)?.localizedDescription ?? "no details"
        return "\(what): \(detail)"
    }

    /// The key window's root view controller, or the topmost one it presents.
    private static func presentingViewController() -> UIViewController? {
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        let window = scenes.flatMap(\.windows).first(where: \.isKeyWindow) ?? scenes.first?.windows.first
        var top = window?.rootViewController
        while let next = top?.presentedViewController { top = next }
        return top
    }
}
