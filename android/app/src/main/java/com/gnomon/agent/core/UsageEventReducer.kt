package com.gnomon.agent.core

data class AppUsageEvent(val timestamp: Long, val packageName: String, val foreground: Boolean)

object UsageEventReducer {
    /** Replays ordered UsageStats events. Gaps without events deliberately add no usage. */
    fun currentPackage(events: List<AppUsageEvent>, initial: String? = null): String? {
        var current = initial
        events.sortedBy { it.timestamp }.forEach { event ->
            if (event.foreground) current = event.packageName
            else if (current == event.packageName) current = null
        }
        return current
    }
}
