package com.gnomon.agent.network

import android.util.Log
import com.gnomon.agent.GnomonApplication
import com.gnomon.agent.data.PendingDeltaEntity
import com.gnomon.agent.data.Repository
import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.RulesMap
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.serialization.json.*
import okhttp3.*
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

class HaClient(
    private val app: GnomonApplication,
    private val repository: Repository,
    private val scope: CoroutineScope
) : WebSocketListener() {
    private val http = OkHttpClient.Builder().pingInterval(30, TimeUnit.SECONDS).build()
    private val json = Json { ignoreUnknownKeys = true }
    private val ids = AtomicInteger()
    private var socket: WebSocket? = null
    private var config: AgentConfig? = null
    private var expectedRulesId = 0
    private val usageInFlight = mutableMapOf<Int, Long>()
    private var heartbeatJob: Job? = null
    private var retryJob: Job? = null
    private var intentionalClose = false
    val rules = MutableStateFlow(RulesMap())
    val connected = MutableStateFlow(false)
    private var testResult: CompletableDeferred<Result<Unit>>? = null

    suspend fun start(value: AgentConfig) {
        intentionalClose = false
        config = value; rules.value = repository.rules()
        open()
    }

    private fun open() {
        val value = config ?: return
        socket?.cancel()
        socket = http.newWebSocket(Request.Builder().url(webSocketUrl(value.haUrl)).build(), this)
    }

    override fun onMessage(webSocket: WebSocket, text: String) {
        scope.launch {
            val message = runCatching { json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return@launch
            when (message["type"]?.jsonPrimitive?.content) {
                "auth_required" -> webSocket.send(Protocol.auth(config!!.token))
                "auth_ok" -> authenticated()
                "auth_invalid" -> {
                    connected.value = false; testResult?.complete(Result.failure(SecurityException("Token rejected")))
                    retry(hours = 1)
                }
                "event" -> {
                    val data = message["event"]?.jsonObject?.get("data")?.jsonObject
                    when (data?.get("kind")?.jsonPrimitive?.contentOrNull) {
                        "rules" -> requestRules()
                        "status" -> if (data["kid"]?.jsonPrimitive?.contentOrNull == config?.kid) refreshStatus()
                    }
                }
                "result" -> handleResult(message)
            }
        }
    }

    private suspend fun authenticated() {
        connected.value = true
        app.status.value = app.status.value.copy(connected = true)
        requestRules()
        socket?.send(Protocol.subscribe(nextId()))
        socket?.send(Protocol.heartbeat(nextId(), config!!))
        heartbeatJob?.cancel(); heartbeatJob = scope.launch {
            while (isActive) { delay(5 * 60_000L); socket?.send(Protocol.heartbeat(nextId(), config!!)) }
        }
        flushNext()
    }

    private suspend fun handleResult(message: JsonObject) {
        val id = message["id"]?.jsonPrimitive?.intOrNull ?: return
        if (id == expectedRulesId) {
            val result = message["result"]?.jsonObject
            val payload = result?.get("response") ?: result
            runCatching { json.decodeFromJsonElement<RulesMap>(payload!!) }.onSuccess {
                rules.value = it; repository.saveRules(it)
                app.status.value = app.status.value.copy(rulesVersion = it.version)
                testResult?.complete(Result.success(Unit))
            }.onFailure { testResult?.complete(Result.failure(it)) }
        }
        usageInFlight.remove(id)?.let { pendingId ->
            if (message["success"]?.jsonPrimitive?.booleanOrNull == true) repository.deletePending(pendingId)
            flushNext()
        }
    }

    private fun requestRules() { expectedRulesId = nextId(); socket?.send(Protocol.rules(expectedRulesId)) }

    private suspend fun refreshStatus() {
        val value = config ?: return
        runCatching { HaRestClient().today(value) }.onSuccess {
            app.status.value = app.status.value.copy(
                today = it.categories, categoryNames = it.categoryNames,
                childOverall = it.child, deviceOverall = it.device
            )
        }
    }

    suspend fun notifyQueueChanged() { if (connected.value) flushNext() }

    private suspend fun flushNext() {
        if (!connected.value || usageInFlight.isNotEmpty()) return
        val value = repository.pending().firstOrNull() ?: return
        val id = nextId(); usageInFlight[id] = value.id
        if (socket?.send(Protocol.usage(id, value)) != true) usageInFlight.remove(id)
        app.status.value = app.status.value.copy(pendingCount = repository.pendingCount(), queueOverflowed = repository.queueOverflowed)
    }

    suspend fun test(value: AgentConfig): Result<Unit> {
        testResult = CompletableDeferred(); start(value)
        return withTimeoutOrNull(15_000) { testResult!!.await() } ?: Result.failure(Exception("Connection timed out"))
    }

    override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
        usageInFlight.clear()
        connected.value = false; app.status.value = app.status.value.copy(connected = false)
        testResult?.complete(Result.failure(t)); if (!intentionalClose) retry()
    }
    override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
        usageInFlight.clear()
        connected.value = false; app.status.value = app.status.value.copy(connected = false)
        if (!intentionalClose) retry()
    }
    private fun retry(hours: Int = 0) {
        retryJob?.cancel(); retryJob = scope.launch {
            var delayMs = if (hours > 0) hours * 3_600_000L else 5_000L
            while (isActive && !connected.value) {
                delay((delayMs * (0.85 + Math.random() * .3)).toLong()); open()
                delayMs = if (hours > 0) delayMs else minOf(300_000L, delayMs * 2)
            }
        }
    }
    fun close() { intentionalClose = true; retryJob?.cancel(); heartbeatJob?.cancel(); socket?.close(1000, "screen off"); socket = null }
    private fun nextId() = ids.incrementAndGet()
    private fun webSocketUrl(value: String): String {
        var base = value.trimEnd('/')
        if (!base.endsWith("/api/websocket")) base += "/api/websocket"
        return base.replaceFirst("https://", "wss://").replaceFirst("http://", "ws://")
    }
}
