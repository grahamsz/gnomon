package com.gnomon.agent.network

import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.AggregateStatus
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
    suspend fun today(config: AgentConfig) = withContext(Dispatchers.IO) {
        val status = callForStatus(config)
        val categories = status.categories.associate { it.id to (it.used to it.limit) }
        val names = status.categories.associate { it.id to it.name }
        TodayStatus(
            categories, names,
            status.child.used to status.child.limit,
            status.device.used to status.device.limit
        )
    }
    private fun callForStatus(config: AgentConfig): AggregateStatus {
        val data = buildJsonObject { put("kid", config.kid); put("device", config.device) }
        val request = serviceRequest(config, "get_status", data)
        return client.newCall(request).execute().use { response ->
            val payload = responsePayload(response)
            json.decodeFromJsonElement(payload)
        }
    }
    suspend fun setClassification(
        config: AgentConfig, item: ClassificationItem, category: String
    ): RulesMap = withContext(Dispatchers.IO) {
        callForRules(config, "set_classification", buildJsonObject {
            put("kid", config.kid); put("kind", item.kind); put("id", item.id); put("category", category)
        })
    }
    private fun callForRules(config: AgentConfig, service: String, data: JsonObject): RulesMap {
        val request = serviceRequest(config, service, data)
        return client.newCall(request).execute().use { response ->
            json.decodeFromJsonElement(responsePayload(response))
        }
    }
    private fun serviceRequest(config: AgentConfig, service: String, data: JsonObject) =
        Request.Builder().url("${baseUrl(config)}/api/services/gnomon/$service?return_response")
            .header("Authorization", "Bearer ${config.token}")
            .post(data.toString().toRequestBody("application/json".toMediaType())).build()

    private fun responsePayload(response: okhttp3.Response): JsonElement {
        val text = response.body?.string().orEmpty()
        if (!response.isSuccessful) error("Home Assistant returned ${response.code}: ${text.take(160)}")
        val root = json.parseToJsonElement(text).jsonObject
        return root["service_response"] ?: root["response"] ?: root
    }
    private fun baseUrl(config: AgentConfig) = config.haUrl.trimEnd('/')
        .replaceFirst("wss://", "https://").replaceFirst("ws://", "http://")
        .removeSuffix("/api/websocket")
}

data class TodayStatus(
    val categories: Map<String, Pair<Int, Int>>,
    val categoryNames: Map<String, String>,
    val child: Pair<Int, Int>,
    val device: Pair<Int, Int>
)
