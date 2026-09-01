package com.gnomon.agent.network

import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.ClassificationCatalog
import com.gnomon.agent.model.ClassificationItem
import com.gnomon.agent.model.RulesMap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.*
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody

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
    suspend fun classifications(config: AgentConfig): ClassificationCatalog = withContext(Dispatchers.IO) {
        callForCatalog(config, "get_classifications", buildJsonObject { put("kid", config.kid) })
    }
    suspend fun setClassification(
        config: AgentConfig, item: ClassificationItem, category: String
    ): ClassificationCatalog = withContext(Dispatchers.IO) {
        callForCatalog(config, "set_classification", buildJsonObject {
            put("kid", config.kid); put("kind", item.kind); put("id", item.id); put("category", category)
        })
    }
    private fun callForCatalog(config: AgentConfig, service: String, data: JsonObject): ClassificationCatalog {
        val request = Request.Builder().url("${baseUrl(config)}/api/services/gnomon/$service?return_response")
            .header("Authorization", "Bearer ${config.token}")
            .post(data.toString().toRequestBody("application/json".toMediaType())).build()
        return client.newCall(request).execute().use { response ->
            val text = response.body?.string().orEmpty()
            if (!response.isSuccessful) error("Home Assistant returned ${response.code}: ${text.take(160)}")
            val root = json.parseToJsonElement(text).jsonObject
            val payload = root["service_response"] ?: root["response"] ?: root
            json.decodeFromJsonElement(payload)
        }
    }
    private fun state(config: AgentConfig, entityId: String): Int {
        val request = Request.Builder().url("${baseUrl(config)}/api/states/$entityId")
            .header("Authorization", "Bearer ${config.token}").build()
        return client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) return@use 0
            val body = json.parseToJsonElement(response.body!!.string()).jsonObject
            body["state"]?.jsonPrimitive?.content?.toDoubleOrNull()?.toInt() ?: 0
        }
    }
    private fun baseUrl(config: AgentConfig) = config.haUrl.trimEnd('/')
        .replaceFirst("wss://", "https://").replaceFirst("ws://", "http://")
        .removeSuffix("/api/websocket")
}
