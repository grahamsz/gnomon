package com.gnomon.agent

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Error
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.gnomon.agent.model.AgentConfig
import com.gnomon.agent.model.ClassificationCategory
import com.gnomon.agent.model.ClassificationItem
import com.gnomon.agent.tracking.hasUsageAccess

class MainActivity : ComponentActivity() {
    private val viewModel: MainViewModel by viewModels()
    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { MaterialTheme { GnomonScreen() } }
    }
    override fun onStop() {
        viewModel.lockAdmin()
        super.onStop()
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable private fun GnomonScreen() {
        val status by viewModel.status.collectAsStateWithLifecycle()
        val saved by viewModel.config.collectAsStateWithLifecycle()
        val message by viewModel.message.collectAsStateWithLifecycle()
        val adminUnlocked by viewModel.adminUnlocked.collectAsStateWithLifecycle()
        val adminPinConfigured by viewModel.adminPinConfigured.collectAsStateWithLifecycle()
        val catalog by viewModel.classifications.collectAsStateWithLifecycle()
        val classificationsLoading by viewModel.classificationsLoading.collectAsStateWithLifecycle()
        var haUrl by remember(saved) { mutableStateOf(saved.haUrl) }
        var token by remember(saved) { mutableStateOf(saved.token) }
        var kid by remember(saved) { mutableStateOf(saved.kid) }
        var device by remember(saved) { mutableStateOf(saved.device) }
        var adminPin by rememberSaveable { mutableStateOf("") }
        var adminPinConfirmation by rememberSaveable { mutableStateOf("") }
        var classificationQuery by rememberSaveable { mutableStateOf("") }
        var tab by remember { mutableIntStateOf(0) }
        val tabs = listOf("Today", "Now", "Unclassified", "Admin", "Status")
        LaunchedEffect(tab, adminUnlocked) {
            if (tab == 3 && adminUnlocked) viewModel.refreshClassifications()
        }
        Scaffold(topBar = { TopAppBar(title = { Text("Gnomon") }) }) { padding ->
            Column(Modifier.padding(padding).fillMaxSize()) {
                ScrollableTabRow(tab) { tabs.forEachIndexed { index, title -> Tab(tab == index, { tab = index }, text = { Text(title) }) } }
                when (tab) {
                    0 -> LazyColumn(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        item { Text("Today's screen time", style = MaterialTheme.typography.headlineSmall) }
                        items(status.today.toList()) { (category, values) ->
                            val used = values.first; val limit = values.second
                            Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(16.dp)) {
                                Text(category, style = MaterialTheme.typography.titleMedium)
                                Text("$used / $limit min · ${if (limit > 0) maxOf(0, limit - used) else "no"} remaining")
                                if (limit > 0) LinearProgressIndicator(progress = { (used.toFloat() / limit).coerceIn(0f, 1f) }, modifier = Modifier.fillMaxWidth())
                            } }
                        }
                        item { Button(onClick = viewModel::refreshToday) { Text("Refresh from Home Assistant") } }
                    }
                    1 -> Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Now", style = MaterialTheme.typography.headlineSmall)
                        Text(status.currentLabel.ifBlank { status.currentPackage }, style = MaterialTheme.typography.titleLarge)
                        Text("${status.category} · ${if (status.counting) "counting" else "not counting"}")
                        Text(if (status.screenOn) "Screen on, app foreground" else "Screen off — never counted")
                    }
                    2 -> LazyColumn(Modifier.padding(16.dp)) {
                        item { Text("Currently unclassified", style = MaterialTheme.typography.headlineSmall) }
                        items(status.unknowns.toList()) { Text(it, Modifier.padding(vertical = 8.dp)) }
                        if (status.unknowns.isEmpty()) item { Text("Nothing waiting for classification.") }
                    }
                    3 -> if (!adminUnlocked) LazyColumn(
                        Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        item { Text("Parent controls", style = MaterialTheme.typography.headlineSmall) }
                        item { Text(if (adminPinConfigured)
                            "Enter the parent PIN to manage the Home Assistant connection and classifications."
                        else "Create a parent PIN before configuring this device. The PIN is stored as a salted hash on this phone.") }
                        item { OutlinedTextField(
                            adminPin, { adminPin = it.filter(Char::isDigit).take(8) },
                            label = { Text(if (adminPinConfigured) "Parent PIN" else "New parent PIN") },
                            visualTransformation = PasswordVisualTransformation(),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                            modifier = Modifier.fillMaxWidth()
                        ) }
                        if (!adminPinConfigured) item { OutlinedTextField(
                            adminPinConfirmation, { adminPinConfirmation = it.filter(Char::isDigit).take(8) },
                            label = { Text("Confirm parent PIN") },
                            visualTransformation = PasswordVisualTransformation(),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                            modifier = Modifier.fillMaxWidth()
                        ) }
                        item { Button(onClick = {
                            if (adminPinConfigured) viewModel.unlockAdmin(adminPin)
                            else viewModel.createAdminPin(adminPin, adminPinConfirmation)
                            adminPin = ""; adminPinConfirmation = ""
                        }) { Text(if (adminPinConfigured) "Unlock" else "Create PIN and unlock") } }
                        if (message.isNotBlank()) item { Text(message) }
                    } else LazyColumn(
                        Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        item { Row(
                            Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column { Text("Parent controls", style = MaterialTheme.typography.headlineSmall); Text("Unlocked", color = Color(0xff188038)) }
                            OutlinedButton(onClick = viewModel::lockAdmin) { Text("Lock") }
                        } }
                        item { HorizontalDivider(); Text("Connection", style = MaterialTheme.typography.titleLarge) }
                        item { OutlinedTextField(haUrl, { haUrl = it }, label = { Text("Home Assistant URL") }, modifier = Modifier.fillMaxWidth()) }
                        item { OutlinedTextField(
                            token, { token = it }, label = { Text("Long-lived token") },
                            visualTransformation = PasswordVisualTransformation(), modifier = Modifier.fillMaxWidth()
                        ) }
                        item { OutlinedTextField(kid, { kid = it.lowercase() }, label = { Text("Kid ID") }, modifier = Modifier.fillMaxWidth()) }
                        item { OutlinedTextField(device, { device = it.lowercase() }, label = { Text("Device ID") }, modifier = Modifier.fillMaxWidth()) }
                        item { Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            val value = AgentConfig(haUrl, token, kid, device)
                            OutlinedButton(onClick = { viewModel.test(value) }) { Text("Test") }
                            Button(onClick = { viewModel.saveAndStart(value) }) { Text("Save and start") }
                        } }
                        item { HorizontalDivider(); Row(
                            Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column { Text("Classifications", style = MaterialTheme.typography.titleLarge); Text("${catalog.items.size} apps and websites · rules v${catalog.version}", style = MaterialTheme.typography.bodySmall) }
                            TextButton(onClick = viewModel::refreshClassifications, enabled = !classificationsLoading) { Text(if (classificationsLoading) "Loading…" else "Refresh") }
                        } }
                        item { OutlinedTextField(
                            classificationQuery, { classificationQuery = it }, label = { Text("Search apps and websites") },
                            modifier = Modifier.fillMaxWidth()
                        ) }
                        val visibleItems = catalog.items.filter {
                            classificationQuery.isBlank() || it.label.contains(classificationQuery, ignoreCase = true) ||
                                it.id.contains(classificationQuery, ignoreCase = true)
                        }
                        items(visibleItems, key = { "${it.kind}:${it.id}" }) { item ->
                            ClassificationCard(item, catalog.categories) { category ->
                                viewModel.assignClassification(item, category)
                            }
                        }
                        if (visibleItems.isEmpty()) item { Text(
                            if (classificationsLoading) "Loading activity…" else "No tracked apps or websites match this search.",
                            color = MaterialTheme.colorScheme.secondary
                        ) }
                        if (message.isNotBlank()) item { Text(message) }
                        item { Text("Bucket changes apply to future minutes and sync through Home Assistant to every Gnomon agent.", color = MaterialTheme.colorScheme.secondary) }
                    }
                    4 -> LazyColumn(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        item { Text("Permissions and status", style = MaterialTheme.typography.headlineSmall) }
                        item { PermissionRow("Usage Access", hasUsageAccess(), "Required to identify the foreground app") {
                            startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
                        } }
                        item { PermissionRow("Battery exemption", batteryExempt(), "Helps tracking survive aggressive power management") {
                            startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
                        } }
                        if (Build.VERSION.SDK_INT >= 33) item { PermissionRow("Notifications", notificationGranted(), "Required for the visible tracking notification") {
                            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                        } }
                        item { Text("HA: ${if (status.connected) "connected" else "offline"} · rules v${status.rulesVersion}\nPending deltas: ${status.pendingCount} · watchdog starts: ${status.restartCount}") }
                        if (status.queueOverflowed) item { Text("The offline queue filled; oldest rows were dropped.", color = MaterialTheme.colorScheme.error) }
                        item { Text("Gnomon is intentionally visible and measurement-only. It cannot block, suspend, or hide apps.", color = MaterialTheme.colorScheme.secondary) }
                    }
                }
            }
        }
    }

    @Composable private fun ClassificationCard(
        item: ClassificationItem,
        categories: List<ClassificationCategory>,
        assign: (String) -> Unit
    ) {
        var expanded by remember { mutableStateOf(false) }
        val categoryName = categories.firstOrNull { it.id == item.category }?.name ?: item.category
        Card(
            Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = if (item.unclassified) Color(0xfffff4dd) else MaterialTheme.colorScheme.surfaceVariant)
        ) { Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Column(Modifier.weight(1f)) {
                    Text(item.label, style = MaterialTheme.typography.titleMedium)
                    Text(item.id, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.secondary)
                }
                Text("${item.minutes} min", style = MaterialTheme.typography.titleMedium)
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Text(if (item.kind == "domain") "Website" else "App", style = MaterialTheme.typography.labelMedium)
                Box {
                    OutlinedButton(onClick = { expanded = true }) { Text(categoryName) }
                    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                        categories.forEach { category -> DropdownMenuItem(
                            text = { Text(category.name) },
                            onClick = { expanded = false; assign(category.id) }
                        ) }
                    }
                }
            }
        } }
    }

    @Composable private fun PermissionRow(name: String, granted: Boolean, why: String, fix: () -> Unit) {
        Card(Modifier.fillMaxWidth()) { Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(if (granted) Icons.Default.CheckCircle else Icons.Default.Error, null, tint = if (granted) Color(0xff188038) else MaterialTheme.colorScheme.error)
            Column(Modifier.padding(horizontal = 12.dp).weight(1f)) { Text(name, style = MaterialTheme.typography.titleMedium); Text(why, style = MaterialTheme.typography.bodySmall) }
            if (!granted) TextButton(onClick = fix) { Text("Fix") }
        } }
    }
    private fun batteryExempt() = getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(packageName)
    private fun notificationGranted() = Build.VERSION.SDK_INT < 33 || ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
}
