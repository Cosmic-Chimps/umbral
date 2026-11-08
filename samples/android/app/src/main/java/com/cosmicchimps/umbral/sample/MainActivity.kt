package com.cosmicchimps.umbral.sample

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts.StartActivityForResult
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

private enum class Screen { Home, Public, Private }

class MainActivity : ComponentActivity() {

    private lateinit var auth: UmbralAuth

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        auth = UmbralAuth(this)
        setContent {
            MaterialTheme {
                Surface(Modifier.fillMaxSize()) { App(auth) }
            }
        }
    }

    override fun onDestroy() {
        auth.dispose() // releases AppAuth's Custom Tab connection
        super.onDestroy()
    }
}

@Composable
private fun App(auth: UmbralAuth) {
    var screen by rememberSaveable { mutableStateOf(Screen.Home) }
    // Where to go once a sign-in succeeds: Home for "Sign in", Private for opening Private.
    var afterSignIn by rememberSaveable { mutableStateOf(Screen.Home) }
    var message by remember { mutableStateOf<String?>(null) }
    // Null means signed out. Re-read whenever the screen changes: the id_token may have expired.
    var claims by remember { mutableStateOf(auth.claims()) }
    LaunchedEffect(screen) { claims = auth.claims() }

    val signInLauncher = rememberLauncherForActivityResult(StartActivityForResult()) { result ->
        auth.finishSignIn(result.data) { error ->
            claims = auth.claims()
            message = error
            if (error == null) screen = afterSignIn
        }
    }
    val signOutLauncher = rememberLauncherForActivityResult(StartActivityForResult()) { result ->
        message = auth.finishSignOut(result.data)
        screen = Screen.Home
    }

    fun signIn(target: Screen) {
        if (BuildConfig.UMBRAL_CLIENT_ID.isBlank()) {
            message = "Set UMBRAL_CLIENT_ID in umbral.properties, then rebuild."
            return
        }
        message = null
        afterSignIn = target
        auth.startSignIn(
            onIntent = { signInLauncher.launch(it) },
            onError = { message = it },
        )
    }

    BackHandler(enabled = screen != Screen.Home) { screen = Screen.Home }

    Column(
        Modifier.fillMaxSize().safeDrawingPadding().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        when (screen) {
            Screen.Home -> {
                Text("Umbral sample", style = MaterialTheme.typography.headlineMedium)
                Button(onClick = { message = null; screen = Screen.Public }) { Text("Public") }
                Button(onClick = {
                    if (claims != null) { message = null; screen = Screen.Private } else signIn(Screen.Private)
                }) { Text("Private") }
                if (claims == null) {
                    Button(onClick = { signIn(Screen.Home) }) { Text("Sign in") }
                } else {
                    Button(onClick = {
                        message = null
                        val intent = auth.signOut()
                        claims = null
                        if (intent != null) signOutLauncher.launch(intent)
                    }) { Text("Sign out") }
                }
            }
            Screen.Public -> {
                Text("Public", style = MaterialTheme.typography.headlineMedium)
                Text("This page is open to anyone, signed in or not.")
            }
            Screen.Private -> {
                Text("Private", style = MaterialTheme.typography.headlineMedium)
                val shown = claims
                if (shown == null) {
                    Text("Your session ended. Go back and sign in again.")
                } else {
                    ClaimsTable(shown)
                }
            }
        }
        message?.let { Text(it, color = MaterialTheme.colorScheme.error) }
    }
}

@Composable
private fun ClaimsTable(claims: Map<String, String>) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        claims.forEach { (name, value) ->
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                Text(name, Modifier.weight(0.35f), style = MaterialTheme.typography.labelLarge)
                Text(value, Modifier.weight(0.65f))
            }
        }
    }
}
