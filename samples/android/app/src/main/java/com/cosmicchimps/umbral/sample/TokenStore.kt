package com.cosmicchimps.umbral.sample

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import android.util.Log
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import net.openid.appauth.AuthState

/**
 * Keeps the AppAuth state (tokens included) between launches.
 *
 * The state is encrypted with an AES-256-GCM key that lives in the Android Keystore and never
 * leaves it. The SharedPreferences file holds only base64(iv + ciphertext), so the tokens are
 * never on disk in the clear. If the key is gone (an app restore, a lock-screen reset), the
 * stored value is useless: we drop it and the person is simply signed out.
 */
class TokenStore(context: Context) {

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun load(): AuthState? {
        val stored = prefs.getString(KEY_STATE, null) ?: return null
        return try {
            AuthState.jsonDeserialize(decrypt(Base64.decode(stored, Base64.NO_WRAP)))
        } catch (e: Exception) {
            Log.w(TAG, "Stored sign-in state is unreadable, treating as signed out", e)
            clear()
            null
        }
    }

    fun save(state: AuthState) {
        val sealed = encrypt(state.jsonSerializeString())
        prefs.edit().putString(KEY_STATE, Base64.encodeToString(sealed, Base64.NO_WRAP)).apply()
    }

    fun clear() {
        prefs.edit().remove(KEY_STATE).apply()
    }

    private fun encrypt(plain: String): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key()) // the Keystore picks a fresh random IV
        return cipher.iv + cipher.doFinal(plain.toByteArray(Charsets.UTF_8))
    }

    private fun decrypt(sealed: ByteArray): String {
        val iv = sealed.copyOfRange(0, IV_BYTES)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(TAG_BITS, iv))
        return String(cipher.doFinal(sealed, IV_BYTES, sealed.size - IV_BYTES), Charsets.UTF_8)
    }

    private fun key(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setKeySize(256)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build(),
        )
        return generator.generateKey()
    }

    private companion object {
        const val TAG = "UmbralSample"
        const val PREFS = "umbral_sample_auth"
        const val KEY_STATE = "auth_state"
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val KEY_ALIAS = "umbral-sample-auth-state"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_BYTES = 12
        const val TAG_BITS = 128
    }
}
