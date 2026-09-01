package com.gnomon.agent

import android.app.Application
import android.content.Intent
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.ClassificationCatalog
import com.gnomon.agent.model.ClassificationCategory
import com.gnomon.agent.model.ClassificationItem
import com.gnomon.agent.model.RulesMap
import com.gnomon.agent.network.HaClient
import com.gnomon.agent.network.HaRestClient
import com.gnomon.agent.tracking.TrackingService
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val app = application as GnomonApplication
    val status = app.status
    val config = MutableStateFlow(AgentConfig())
    val message = MutableStateFlow("")
    val adminUnlocked = MutableStateFlow(false)
    val adminPinConfigured = MutableStateFlow(app.adminLock.hasPin())
    val classifications = MutableStateFlow(ClassificationCatalog())
    val classificationsLoading = MutableStateFlow(false)
    init { viewModelScope.launch { config.value = app.repository.config() ?: AgentConfig() } }

    fun saveAndStart(value: AgentConfig) = viewModelScope.launch {
        app.repository.saveConfig(value); config.value = value
        ContextCompat.startForegroundService(app, Intent(app, TrackingService::class.java))
        message.value = "Saved. Tracking service started."
    }
    fun test(value: AgentConfig) = viewModelScope.launch {
        message.value = "Testing Home Assistant…"
        val client = HaClient(app, app.repository, viewModelScope)
        val result = client.test(value); client.close()
        message.value = result.fold({ "Connection and get_rules succeeded." }, { "Connection failed: ${it.message}" })
    }
    fun refreshToday() = viewModelScope.launch {
        val value = config.value
        runCatching { HaRestClient().today(value) }
            .onSuccess { status.value = status.value.copy(
                today = it.categories, categoryNames = it.categoryNames,
                childOverall = it.child, deviceOverall = it.device
            ); message.value = "Today refreshed." }
            .onFailure { message.value = "Refresh failed: ${it.message}" }
    }
    fun createAdminPin(pin: String, confirmation: String) {
        if (pin != confirmation) { message.value = "PINs do not match."; return }
        runCatching { app.adminLock.setPin(pin) }
            .onSuccess {
                adminPinConfigured.value = true; adminUnlocked.value = true
                message.value = "Parent PIN created."
                refreshClassifications()
            }
            .onFailure { message.value = it.message.orEmpty() }
    }
    fun unlockAdmin(pin: String) {
        if (app.adminLock.verify(pin)) {
            adminUnlocked.value = true; message.value = "Admin controls unlocked."
            refreshClassifications()
        } else message.value = "Incorrect parent PIN."
    }
    fun lockAdmin() { adminUnlocked.value = false }
    fun refreshClassifications() = viewModelScope.launch {
        if (!adminUnlocked.value) return@launch
        classificationsLoading.value = true
        runCatching { buildLocalCatalog(app.repository.rules()) }
            .onSuccess { classifications.value = it; message.value = "Local activity refreshed." }
            .onFailure { message.value = "Classification refresh failed: ${it.message}" }
        classificationsLoading.value = false
    }
    fun assignClassification(item: ClassificationItem, category: String) = viewModelScope.launch {
        if (!adminUnlocked.value || category == item.category) return@launch
        classificationsLoading.value = true
        runCatching { HaRestClient().setClassification(config.value, item, category) }
            .onSuccess { rules ->
                app.repository.saveRules(rules)
                classifications.value = buildLocalCatalog(rules)
                message.value = "${item.label} now uses $category. Syncing to agents."
            }
            .onFailure { message.value = "Could not change bucket: ${it.message}" }
        classificationsLoading.value = false
    }

    private suspend fun buildLocalCatalog(rules: RulesMap): ClassificationCatalog {
        val kid = config.value.kid
        val override = rules.overrides[kid]
        val items = app.repository.activity().map { value ->
            val category = if (value.kind == "domain") {
                val candidates = rules.domains + (override?.domains ?: emptyMap())
                candidates.filterKeys { value.itemId == it || value.itemId.endsWith(".$it") }
                    .maxByOrNull { it.key.length }?.value ?: "unclassified"
            } else override?.processes?.get(value.itemId) ?: rules.processes[value.itemId] ?: "unclassified"
            ClassificationItem(
                value.kind, value.itemId, value.label, category, value.minutes,
                listOf(config.value.device), value.lastSeen.toString(), category == "unclassified"
            )
        }
        return ClassificationCatalog(
            rules.version, rules.categories.map { ClassificationCategory(it.id, it.name) }, items
        )
    }
}
