package com.gnomon.agent.tracking

import android.content.*
import androidx.core.content.ContextCompat
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.gnomon.agent.GnomonApplication

class WatchdogWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        val app = applicationContext as GnomonApplication
        app.status.value = app.status.value.copy(restartCount = app.status.value.restartCount + 1)
        ContextCompat.startForegroundService(applicationContext, Intent(applicationContext, TrackingService::class.java))
        return Result.success()
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED)
            ContextCompat.startForegroundService(context, Intent(context, TrackingService::class.java))
    }
}
