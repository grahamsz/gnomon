package com.gnomon.agent

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.gnomon.agent.core.AppUsageEvent
import com.gnomon.agent.core.UsageEventReducer
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class UsageEventReducerInstrumentedTest {
    @Test fun replayAcrossGapUsesLastForegroundEvent() {
        assertEquals("com.example.two", UsageEventReducer.currentPackage(listOf(
            AppUsageEvent(100, "com.example.one", true),
            AppUsageEvent(200, "com.example.one", false),
            AppUsageEvent(201, "com.example.two", true)
        )))
    }
}
