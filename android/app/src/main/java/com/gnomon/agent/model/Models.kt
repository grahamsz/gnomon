package com.gnomon.agent.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable data class AgentConfig(
    val haUrl: String = "", val token: String = "", val kid: String = "",
    val device: String = "phone"
)

@Serializable data class CategoryRule(
    val id: String, val name: String,
    @SerialName("idle_timeout_min") val idleTimeoutMinutes: Int = 3,
    @SerialName("media_counts_as_active") val mediaCountsAsActive: Boolean = false
)

@Serializable data class RuleOverrides(
    val processes: Map<String, String> = emptyMap(),
    val domains: Map<String, String> = emptyMap()
)

@Serializable data class RulesMap(
    val version: Int = 0,
    val categories: List<CategoryRule> = listOf(CategoryRule("unclassified", "Unclassified")),
    val processes: Map<String, String> = emptyMap(),
    val domains: Map<String, String> = emptyMap(),
    val overrides: Map<String, RuleOverrides> = emptyMap()
)

data class Classification(val category: String, val packageName: String, val unknown: Boolean, val rulesVersion: Int)

data class TrackerStatus(
    val currentPackage: String = "None", val currentLabel: String = "",
    val category: String = "unclassified", val counting: Boolean = false,
    val screenOn: Boolean = true, val connected: Boolean = false,
    val rulesVersion: Int = 0, val pendingCount: Int = 0,
    val usageAccess: Boolean = false, val restartCount: Int = 0,
    val queueOverflowed: Boolean = false, val unknowns: Set<String> = emptySet(),
    val today: Map<String, Pair<Int, Int>> = emptyMap()
)
