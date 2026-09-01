package com.gnomon.agent

import android.app.Application
import androidx.work.*
import com.gnomon.agent.data.GnomonDatabase
import com.gnomon.agent.data.AdminLock
import com.gnomon.agent.data.Repository
import com.gnomon.agent.model.TrackerStatus
import com.gnomon.agent.tracking.WatchdogWorker
import kotlinx.coroutines.flow.MutableStateFlow
import java.util.concurrent.TimeUnit

class GnomonApplication : Application() {
    lateinit var repository: Repository; private set
    lateinit var adminLock: AdminLock; private set
    val status = MutableStateFlow(TrackerStatus())
    override fun onCreate() {
        super.onCreate()
        repository = Repository(GnomonDatabase.get(this))
        adminLock = AdminLock(this)
        val request = PeriodicWorkRequestBuilder<WatchdogWorker>(15, TimeUnit.MINUTES, 5, TimeUnit.MINUTES).build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork("gnomon-watchdog", ExistingPeriodicWorkPolicy.UPDATE, request)
    }
}
