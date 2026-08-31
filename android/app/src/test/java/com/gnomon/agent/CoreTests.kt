package com.gnomon.agent

import com.gnomon.agent.core.*
import com.gnomon.agent.model.*
import org.junit.Assert.*
import org.junit.Test

class CoreTests {
    @Test fun classifierIsExactAndHonorsKidOverride() {
        val rules = RulesMap(7, processes = mapOf("com.game.app" to "games"), overrides = mapOf(
            "alex" to RuleOverrides(processes = mapOf("com.school.app" to "schoolwork"))
        ))
        val classifier = Classifier()
        assertEquals("games", classifier.classify("com.game.app", "alex", rules).category)
        assertEquals("schoolwork", classifier.classify("com.school.app", "alex", rules).category)
        assertTrue(classifier.classify("game.app", "alex", rules).unknown)
    }

    @Test fun deltaQuantizerKeepsRemainder() {
        val value = DeltaQuantizer(); value.accumulate("games", 125_000)
        assertEquals(2, value.flush("games")); assertEquals(5_000, value.remainderMillis("games"))
    }

    @Test fun queueCapDropsOnlyOldestExcess() {
        assertEquals(0, QueueCapPolicy.rowsToDrop(719))
        assertEquals(1, QueueCapPolicy.rowsToDrop(720))
        assertEquals(6, QueueCapPolicy.rowsToDrop(725))
    }

    @Test fun eventReducerHandlesRapidSwitching() {
        val events = listOf(
            AppUsageEvent(1, "a", true), AppUsageEvent(2, "a", false),
            AppUsageEvent(3, "b", true), AppUsageEvent(4, "c", true), AppUsageEvent(5, "c", false)
        )
        assertNull(UsageEventReducer.currentPackage(events))
    }
}
