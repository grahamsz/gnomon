package com.gnomon.agent.tracking

import android.app.AppOpsManager
import android.content.Context
import android.os.Process

fun Context.hasUsageAccess(): Boolean {
    val appOps = getSystemService(AppOpsManager::class.java)
    return appOps.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), packageName) == AppOpsManager.MODE_ALLOWED
}
