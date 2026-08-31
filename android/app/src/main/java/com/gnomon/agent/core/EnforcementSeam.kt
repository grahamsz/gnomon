package com.gnomon.agent.core

/** Version 1 seam only. The production implementation intentionally does nothing. */
interface EnforcementController {
    fun onCategoryExhausted(category: String)
    fun onLockdown(state: Boolean)
}

object NoOpEnforcementController : EnforcementController {
    override fun onCategoryExhausted(category: String) = Unit
    override fun onLockdown(state: Boolean) = Unit
}
