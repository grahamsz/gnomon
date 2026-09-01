package com.gnomon.agent

import com.gnomon.agent.data.PendingDeltaEntity
import com.gnomon.agent.network.Protocol
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Test

class ProtocolTests {
    @Test fun usageMessageContainsIntegerDelta() {
        val value = PendingDeltaEntity(1, "alex", "phone", "games", 2, "com.game")
        val message = Json.parseToJsonElement(Protocol.usage(1, value)).jsonObject
        assertEquals("report_usage", message["service"]!!.jsonPrimitive.content)
        assertEquals(2, message["service_data"]!!.jsonObject["minutes"]!!.jsonPrimitive.content.toInt())
        assertEquals("process", message["service_data"]!!.jsonObject["kind"]!!.jsonPrimitive.content)
    }
}
