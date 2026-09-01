package com.gnomon.agent.core

import com.gnomon.agent.model.Classification
import com.gnomon.agent.model.RulesMap

class Classifier {
    fun classify(packageName: String, kid: String, rules: RulesMap): Classification {
        val normalized = packageName.lowercase()
        val category = rules.overrides[kid]?.processes?.get(normalized) ?: rules.processes[normalized]
        return Classification(category ?: "unclassified", normalized, category == null, rules.version)
    }
}

class DeltaQuantizer {
    private val milliseconds = mutableMapOf<String, Long>()
    fun accumulate(category: String, elapsedMillis: Long) {
        if (elapsedMillis > 0) milliseconds[category] = (milliseconds[category] ?: 0) + elapsedMillis
    }
    fun flush(category: String): Int {
        val value = milliseconds[category] ?: 0
        val minutes = (value / 60_000L).toInt()
        milliseconds[category] = value - minutes * 60_000L
        return minutes
    }
    fun remainderMillis(category: String) = milliseconds[category] ?: 0
}

object QueueCapPolicy {
    const val MaximumRows = 720
    fun rowsToDrop(currentRows: Int) = maxOf(0, currentRows - (MaximumRows - 1))
}
