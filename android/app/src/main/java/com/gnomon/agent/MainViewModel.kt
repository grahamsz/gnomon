package com.gnomon.agent

import android.app.Application
import android.content.Intent
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.gnomon.agent.model.AgentConfig
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
        val rules = app.repository.rules(); val value = config.value
        runCatching { HaRestClient().today(value, rules) }
            .onSuccess { status.value = status.value.copy(today = it); message.value = "Today refreshed." }
            .onFailure { message.value = "Refresh failed: ${it.message}" }
    }
}
