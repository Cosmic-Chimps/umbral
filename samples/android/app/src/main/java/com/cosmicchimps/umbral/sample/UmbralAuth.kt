package com.cosmicchimps.umbral.sample

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import net.openid.appauth.AppAuthConfiguration
import net.openid.appauth.AuthState
import net.openid.appauth.AuthorizationException
import net.openid.appauth.AuthorizationRequest
import net.openid.appauth.AuthorizationResponse
import net.openid.appauth.AuthorizationService
import net.openid.appauth.AuthorizationServiceConfiguration
import net.openid.appauth.EndSessionRequest
import net.openid.appauth.ResponseTypeValues
import net.openid.appauth.connectivity.ConnectionBuilder

/**
 * Everything the sample does with AppAuth, in one place.
 *
 * AppAuth does the security-relevant work: it opens a Custom Tab (not an embedded browser), sends a PKCE
 * S256 challenge, a `state` and a `nonce`, checks `state` when the browser comes back, and
 * validates the id_token (issuer, audience, nonce, expiry) during the code exchange. We leave
 * all of that at its defaults.
 */
class UmbralAuth(context: Context) {

    private val tokens = TokenStore(context)

    private val issuer = Uri.parse(BuildConfig.UMBRAL_ISSUER)

    // A client with a loopback http issuer (a local Umbral) must opt out of AppAuth's https check.
    private val service = AuthorizationService(
        context,
        AppAuthConfiguration.Builder()
            .setConnectionBuilder(LoopbackOrHttpsConnectionBuilder)
            .setSkipIssuerHttpsCheck(issuer.scheme == "http" && isLoopback(issuer.host))
            .build(),
    )

    private var authState: AuthState = tokens.load() ?: AuthState()

    /**
     * The claims to show when signed in, null when signed out. Signed in means: the stored state is
     * authorized and its id_token has not expired. There is no refresh token (no `offline_access`),
     * so an expired token ends the session locally and the next sign-in asks again.
     */
    fun claims(): Map<String, String>? {
        // AppAuth already parsed and validated the id_token during the code exchange.
        val idToken = authState.parsedIdToken
        if (!authState.isAuthorized || idToken == null) return null
        if (idToken.expiration <= System.currentTimeMillis() / 1000) {
            discard()
            return null
        }
        val result = linkedMapOf("sub" to idToken.subject)
        // name and idp are always there; email and email_verified only when Umbral sent them.
        for (name in listOf("name", "idp", "email", "email_verified")) {
            idToken.additionalClaims[name]?.let { result[name] = it.toString() }
        }
        return result
    }

    /** Discovers the endpoints from the issuer, then hands back the intent that opens the Custom Tab. */
    fun startSignIn(onIntent: (Intent) -> Unit, onError: (String) -> Unit) {
        // Endpoints come from /.well-known/openid-configuration only, never hard-coded.
        AuthorizationServiceConfiguration.fetchFromIssuer(issuer, { config, ex ->
            if (config == null) {
                onError(describe("Could not reach Umbral", ex))
                return@fetchFromIssuer
            }
            val request = AuthorizationRequest.Builder(
                config,
                BuildConfig.UMBRAL_CLIENT_ID,
                ResponseTypeValues.CODE,
                REDIRECT_URI,
            ).setScopes("openid", "profile", "email").build()
            onIntent(service.getAuthorizationRequestIntent(request))
        }, LoopbackOrHttpsConnectionBuilder)
    }

    /** Takes what the Custom Tab returned, exchanges the code, keeps the tokens. */
    fun finishSignIn(data: Intent?, onDone: (error: String?) -> Unit) {
        val response = data?.let { AuthorizationResponse.fromIntent(it) }
        val exception = data?.let { AuthorizationException.fromIntent(it) }
        if (response == null) {
            onDone(if (exception == null) "Sign-in cancelled" else describe("Sign-in failed", exception))
            return
        }
        authState.update(response, exception)
        // A public client: no client authentication on the token request, PKCE proves it is us.
        service.performTokenRequest(response.createTokenExchangeRequest()) { tokenResponse, tokenException ->
            authState.update(tokenResponse, tokenException)
            if (tokenResponse == null) {
                discard()
                onDone(describe("Token exchange failed", tokenException))
            } else {
                tokens.save(authState)
                onDone(null)
            }
        }
    }

    /**
     * Signs out in the order the contract asks: end the app's own session first, then send the
     * person to Umbral's end-session endpoint. Returns that intent, or null when there is nothing
     * to send the person to. Either way this device is signed out when this returns.
     */
    fun signOut(): Intent? {
        val idToken = authState.idToken
        val config = authState.authorizationServiceConfiguration
        discard()
        if (idToken == null || config?.endSessionEndpoint == null) return null
        val request = EndSessionRequest.Builder(config) // AppAuth adds `state`
            .setIdTokenHint(idToken)
            .setPostLogoutRedirectUri(POST_LOGOUT_REDIRECT_URI)
            .build()
        return service.getEndSessionRequestIntent(request)
    }

    /** Reads the end-session result; null when it went fine (or was cancelled: we are signed out anyway). */
    fun finishSignOut(data: Intent?): String? {
        val exception = data?.let { AuthorizationException.fromIntent(it) }
        return exception?.let { describe("Sign-out at Umbral failed", it) }
    }

    fun dispose() = service.dispose()

    private fun discard() {
        authState = AuthState()
        tokens.clear()
    }

    private fun describe(what: String, ex: AuthorizationException?): String {
        val detail = ex?.errorDescription ?: ex?.error ?: ex?.message ?: "no details"
        Log.w(TAG, "$what: $detail", ex)
        return "$what: $detail"
    }

    companion object {
        private const val TAG = "UmbralSample"

        // One slash after the colon: Umbral matches redirect URIs exactly.
        val REDIRECT_URI: Uri = Uri.parse("com.cosmicchimps.umbral.sample:/callback")
        val POST_LOGOUT_REDIRECT_URI: Uri = Uri.parse("com.cosmicchimps.umbral.sample:/signout-callback")

        fun isLoopback(host: String?) = host in setOf("localhost", "127.0.0.1", "::1", "[::1]")
    }
}

/**
 * https always; http only to a loopback address (a local Umbral reached with `adb reverse`).
 * Umbral itself accepts plain http only on loopback. Redirects are not followed.
 */
private object LoopbackOrHttpsConnectionBuilder : ConnectionBuilder {
    private const val TIMEOUT_MS = 15_000

    override fun openConnection(uri: Uri): HttpURLConnection {
        val allowed = uri.scheme == "https" || (uri.scheme == "http" && UmbralAuth.isLoopback(uri.host))
        if (!allowed) throw IOException("Refusing to connect to $uri: https only (http for loopback)")
        return (URL(uri.toString()).openConnection() as HttpURLConnection).apply {
            connectTimeout = TIMEOUT_MS
            readTimeout = TIMEOUT_MS
            instanceFollowRedirects = false
        }
    }
}
