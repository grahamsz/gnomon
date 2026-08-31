package com.gnomon.agent.data

import androidx.room.withTransaction
import com.gnomon.agent.core.QueueCapPolicy
import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.RulesMap
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

class Repository(private val db: GnomonDatabase) {
    private val dao = db.dao()
    private val json = Json { ignoreUnknownKeys = true }
    var queueOverflowed = false; private set

    suspend fun config() = dao.config()?.let { AgentConfig(it.haUrl, it.token, it.kid, it.device) }
    suspend fun saveConfig(value: AgentConfig) = dao.saveConfig(ConfigEntity(1, value.haUrl, value.token, value.kid, value.device))
    suspend fun rules() = dao.rules()?.let { json.decodeFromString<RulesMap>(it.json) } ?: RulesMap()
    suspend fun saveRules(value: RulesMap) = dao.saveRules(RulesEntity(1, value.version, json.encodeToString(value)))
    suspend fun pending() = dao.pending()
    suspend fun pendingCount() = dao.pendingCount()
    suspend fun deletePending(id: Long) = dao.delete(id)

    suspend fun enqueue(kid: String, device: String, category: String, minutes: Int, appId: String) {
        db.withTransaction {
            val overflow = QueueCapPolicy.rowsToDrop(dao.pendingCount())
            if (overflow > 0) { dao.deleteOldest(overflow); queueOverflowed = true }
            dao.insert(PendingDeltaEntity(kid = kid, device = device, category = category, minutes = minutes, appId = appId))
        }
    }
}
