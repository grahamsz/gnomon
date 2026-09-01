package com.gnomon.agent.tracking

import android.app.*
import android.app.usage.UsageEvents
import android.app.usage.UsageStatsManager
import android.content.*
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.gnomon.agent.GnomonApplication
import com.gnomon.agent.MainActivity
import com.gnomon.agent.core.*
import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.network.HaClient
import com.gnomon.agent.network.HaRestClient
import kotlinx.coroutines.*

class TrackingService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private lateinit var app: GnomonApplication
    private lateinit var client: HaClient
    private val classifier = Classifier(); private val quantizer = DeltaQuantizer()
    private var config = AgentConfig(); private var currentPackage: String? = null; private var currentCategory = "unclassified"
    private var currentLabel = ""
    private var lastQuery = System.currentTimeMillis(); private var lastTick = lastQuery; private var screenOn = true
    private var lastTotalsRefresh = 0L
    private val screenReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            screenOn = intent.action != Intent.ACTION_SCREEN_OFF
            app.status.value = app.status.value.copy(screenOn = screenOn, counting = false)
            if (screenOn) scope.launch { client.start(config) }
            else scope.launch {
                flush()
                if (app.repository.pendingCount() == 0) client.close() else client.notifyQueueChanged()
            }
        }
    }

    override fun onCreate() {
        super.onCreate(); app = application as GnomonApplication
        client = HaClient(app, app.repository, scope)
        createChannel(); startForeground(NOTIFICATION_ID, notification("Starting…"))
        registerReceiver(screenReceiver, IntentFilter().apply {
            addAction(Intent.ACTION_SCREEN_ON); addAction(Intent.ACTION_SCREEN_OFF); addAction(Intent.ACTION_USER_PRESENT)
        })
        scope.launch { runTracker() }
    }

    private suspend fun runTracker() {
        config = app.repository.config() ?: return
        client.start(config)
        while (scope.isActive) {
            val now = System.currentTimeMillis(); val access = hasUsageAccess()
            if (access && screenOn) {
                val observed = queryEvents(lastQuery, now)
                val next = UsageEventReducer.currentPackage(observed, currentPackage)
                if (next != currentPackage) {
                    flush(); currentPackage = next
                    next?.let { app.repository.observeActivity("process", it, appLabel(it)) }
                }
                next?.let { packageName ->
                    val classification = classifier.classify(packageName, config.kid, client.rules.value)
                    currentCategory = classification.category
                    val usageKey = "process:$packageName"
                    quantizer.accumulate(usageKey, now - lastTick)
                    if (quantizer.remainderMillis(usageKey) >= 60_000L) flush()
                    val label = appLabel(packageName)
                    currentLabel = label
                    app.status.value = app.status.value.copy(currentPackage = packageName, currentLabel = label,
                        category = classification.category, counting = true, screenOn = true, usageAccess = access,
                        connected = client.connected.value, rulesVersion = client.rules.value.version)
                }
            } else {
                app.status.value = app.status.value.copy(counting = false, screenOn = screenOn, usageAccess = access)
                if (!screenOn && app.repository.pendingCount() == 0) client.close()
            }
            if (client.connected.value && now - lastTotalsRefresh >= 5 * 60_000L) {
                runCatching { HaRestClient().today(config) }
                    .onSuccess { app.status.value = app.status.value.copy(
                        today = it.categories, categoryNames = it.categoryNames,
                        childOverall = it.child, deviceOverall = it.device
                    ) }
                lastTotalsRefresh = now
            }
            lastTick = now; lastQuery = now
            updateNotification(); delay(15_000L)
        }
    }

    private fun queryEvents(start: Long, end: Long): List<AppUsageEvent> {
        val manager = getSystemService(UsageStatsManager::class.java)
        val events = manager.queryEvents(start, end); val item = UsageEvents.Event(); val result = mutableListOf<AppUsageEvent>()
        while (events.hasNextEvent()) {
            events.getNextEvent(item)
            val foreground = item.eventType == UsageEvents.Event.MOVE_TO_FOREGROUND || item.eventType == UsageEvents.Event.ACTIVITY_RESUMED
            val background = item.eventType == UsageEvents.Event.MOVE_TO_BACKGROUND || item.eventType == UsageEvents.Event.ACTIVITY_PAUSED
            if (foreground || background) result += AppUsageEvent(item.timeStamp, item.packageName, foreground)
        }
        return result
    }

    private suspend fun flush() {
        val packageName = currentPackage ?: return
        val minutes = quantizer.flush("process:$packageName")
        if (minutes > 0) {
            app.repository.observeActivity("process", packageName, currentLabel, minutes)
            val snapshot = app.status.value
            val category = snapshot.today[currentCategory] ?: (0 to 0)
            app.status.value = snapshot.copy(
                today = snapshot.today + (currentCategory to (category.first + minutes to category.second)),
                childOverall = snapshot.childOverall.first + minutes to snapshot.childOverall.second,
                deviceOverall = snapshot.deviceOverall.first + minutes to snapshot.deviceOverall.second
            )
        }
        var remaining = minutes
        while (remaining > 0) {
            val chunk = minOf(30, remaining)
            app.repository.enqueue(
                config.kid, config.device, currentCategory, chunk, packageName,
                kind = "process", appLabel = currentLabel
            ); remaining -= chunk
        }
        if (minutes > 0) client.notifyQueueChanged()
    }
    private fun appLabel(packageName: String) = runCatching {
        packageManager.getApplicationLabel(packageManager.getApplicationInfo(packageName, 0)).toString()
    }.getOrDefault(packageName)
    private fun updateNotification() {
        val value = app.status.value
        val summary = value.today.entries.take(3).joinToString(" · ") { "${it.key} ${it.value.first}/${it.value.second}" }
        val text = summary.ifBlank {
            if (value.counting) "${value.currentLabel}: ${value.category} · counting"
            else "Not counting · ${if (value.screenOn) "no foreground app" else "screen off"}"
        }
        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification(text))
    }
    private fun notification(text: String): Notification {
        val intent = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        return NotificationCompat.Builder(this, CHANNEL).setSmallIcon(com.gnomon.agent.R.drawable.ic_gnomon_notification)
            .setContentTitle("Gnomon screen time").setContentText(text).setOngoing(true).setContentIntent(intent).build()
    }
    private fun createChannel() = getSystemService(NotificationManager::class.java).createNotificationChannel(
        NotificationChannel(CHANNEL, "Gnomon tracking", NotificationManager.IMPORTANCE_LOW)
    )
    override fun onDestroy() { runBlocking { flush() }; unregisterReceiver(screenReceiver); client.close(); scope.cancel(); super.onDestroy() }
    override fun onBind(intent: Intent?): IBinder? = null
    companion object { const val CHANNEL = "gnomon_tracking"; const val NOTIFICATION_ID = 4201 }
}
