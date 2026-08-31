const AGENT = "http://127.0.0.1:45981";
let last = {domain: null, category: "unknown", reachable: false, rulesVersion: 0};

async function reportActiveTab() {
  try {
    const [tab] = await chrome.tabs.query({active: true, lastFocusedWindow: true});
    if (!tab?.url) return;
    const parsed = new URL(tab.url);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return;
    const domain = parsed.hostname.toLowerCase();
    await fetch(`${AGENT}/active-domain`, {
      method: "POST",
      headers: {"content-type": "application/json"},
      // Privacy boundary: hostname is the only browser-derived value serialized.
      body: JSON.stringify({domain})
    });
    const response = await fetch(`${AGENT}/status`);
    const status = await response.json();
    last = {domain, category: status.category ?? "unclassified", reachable: true,
            rulesVersion: status.rulesVersion ?? 0};
  } catch {
    last = {...last, reachable: false};
  }
}

chrome.tabs.onActivated.addListener(reportActiveTab);
chrome.tabs.onUpdated.addListener((_tabId, change) => { if (change.url) reportActiveTab(); });
chrome.windows.onFocusChanged.addListener(reportActiveTab);
chrome.alarms.create("gnomon-heartbeat", {periodInMinutes: 0.25});
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === "gnomon-heartbeat") reportActiveTab(); });
chrome.runtime.onStartup.addListener(reportActiveTab);
chrome.runtime.onInstalled.addListener(reportActiveTab);
chrome.runtime.onMessage.addListener((message, _sender, respond) => {
  if (message === "status") { respond(last); return false; }
  return false;
});
