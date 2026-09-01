package com.gnomon.agent.data

import android.content.Context
import android.util.Base64
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

/** A local parent PIN gate. Only a salted, deliberately expensive hash is stored. */
class AdminLock(context: Context) {
    private val preferences = context.getSharedPreferences("gnomon_admin", Context.MODE_PRIVATE)

    fun hasPin(): Boolean = preferences.contains(HASH)

    fun setPin(pin: String) {
        require(pin.matches(Regex("^[0-9]{4,8}$"))) { "Use a 4–8 digit parent PIN." }
        val salt = ByteArray(16).also(SecureRandom()::nextBytes)
        val hash = derive(pin, salt)
        preferences.edit()
            .putString(SALT, Base64.encodeToString(salt, Base64.NO_WRAP))
            .putString(HASH, Base64.encodeToString(hash, Base64.NO_WRAP))
            .apply()
    }

    fun verify(pin: String): Boolean {
        val salt = preferences.getString(SALT, null)?.let { Base64.decode(it, Base64.NO_WRAP) } ?: return false
        val expected = preferences.getString(HASH, null)?.let { Base64.decode(it, Base64.NO_WRAP) } ?: return false
        return MessageDigest.isEqual(expected, derive(pin, salt))
    }

    private fun derive(pin: String, salt: ByteArray): ByteArray {
        val spec = PBEKeySpec(pin.toCharArray(), salt, 120_000, 256)
        return try { SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).encoded }
        finally { spec.clearPassword() }
    }

    private companion object {
        const val SALT = "pin_salt"
        const val HASH = "pin_hash"
    }
}
