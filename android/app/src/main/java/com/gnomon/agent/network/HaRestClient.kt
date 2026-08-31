package com.gnomon.agent.network

import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.RulesMap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.*
import okhttp3.OkHttpClient
import okhttp3.Request

class HaRestClient {
    private val client = OkHttpClient()
    private val json = Json { ignoreUnknownKeys = true }
    suspend fun today(config: AgentConfig, rules: RulesMap): Map<String, Pair<Int, Int>> = withContext(Dispatchers.IO) {
        rules.categories.associate { category ->
            val used = state(config, "sensor.gnomon_used_${config.kid}_${category.id}")
            val limit = state(config, "number.gnomon_limit_${config.kid}_${category.id}")
            category.id to (used to limit)
        }
    }
    private fun state(config: AgentConfig, entityId: String): Int {
        val base = config.haUrl.trimEnd('/').replaceFirst("wss://", "https://").replaceFirst("ws://", "http://")
            .removeSuffix("/api/websocket")
        val request = Request.Builder().url("$base/api/states/$entityId")
            .header("Authorization", "Bearer ${config.token}").build()
        return client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) return@use 0
            val body = json.parseToJsonElement(response.body!!.string()).jsonObject
            body["state"]?.jsonPrimitive?.content?.toDoubleOrNull()?.toInt() ?: 0
        }
    }
}
