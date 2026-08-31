package com.gnomon.agent.network

import com.gnomon.agent.BuildConfig
import com.gnomon.agent.data.PendingDeltaEntity
import com.gnomon.agent.model.AgentConfig
import kotlinx.serialization.json.*

object Protocol {
    fun auth(token: String) = buildJsonObject { put("type", "auth"); put("access_token", token) }.toString()
    fun getStates(id: Int) = buildJsonObject { put("id", id); put("type", "get_states") }.toString()
    fun subscribe(id: Int) = buildJsonObject {
        put("id", id); put("type", "subscribe_events"); put("event_type", "state_changed")
    }.toString()
    fun call(id: Int, service: String, data: JsonObject, response: Boolean = false) = buildJsonObject {
        put("id", id); put("type", "call_service"); put("domain", "gnomon"); put("service", service)
        put("service_data", data); if (response) put("return_response", true)
    }.toString()
    fun rules(id: Int) = call(id, "get_rules", buildJsonObject {}, true)
    fun heartbeat(id: Int, config: AgentConfig) = call(id, "heartbeat", buildJsonObject {
        put("kid", config.kid); put("device", config.device); put("agent_version", BuildConfig.VERSION_NAME)
    })
    fun usage(id: Int, value: PendingDeltaEntity) = call(id, "report_usage", buildJsonObject {
        put("kid", value.kid); put("device", value.device); put("category", value.category)
        put("minutes", value.minutes); put("app_id", value.appId)
    })
    fun unknown(id: Int, config: AgentConfig, packageName: String, label: String) = call(id, "report_unknown", buildJsonObject {
        put("kid", config.kid); put("device", config.device); put("kind", "process")
        put("id", packageName); put("hint", label.take(120))
    })
}
