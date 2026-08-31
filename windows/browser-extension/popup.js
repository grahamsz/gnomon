chrome.runtime.sendMessage("status", status => {
  document.querySelector("#domain").textContent = status?.domain ?? "No web page reported";
  document.querySelector("#category").textContent = status?.category ?? "unknown";
  const agent = document.querySelector("#agent");
  agent.textContent = status?.reachable ? `Connected · rules v${status.rulesVersion}` : "Not reachable";
  agent.className = status?.reachable ? "ok" : "bad";
});
