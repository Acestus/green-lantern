import Chart from "chart.js/auto";

const correlationId = (window.crypto && window.crypto.randomUUID)
  ? window.crypto.randomUUID()
  : `cid-${Date.now()}`;

let trendChart;
let latencyChart;

function renderRows(targetId, rows, columns) {
  const target = document.getElementById(targetId);
  if (!target) {
    return;
  }

  if (!rows || rows.length === 0) {
    target.innerHTML = `<tr><td colspan="${columns.length}">No rows</td></tr>`;
    return;
  }

  target.innerHTML = rows.map((row) => {
    const cells = columns.map((col) => `<td>${row[col] ?? ""}</td>`).join("");
    return `<tr>${cells}</tr>`;
  }).join("");
}

function setPanelVisibility(panelId, rows) {
  const panel = document.getElementById(panelId);
  if (!panel) {
    return;
  }

  if (Array.isArray(rows) && rows.length > 0) {
    panel.classList.remove("hidden");
    return;
  }

  panel.classList.add("hidden");
}

async function apiFetch(path) {
  const apiBase = window.HELLO_PORTAL_CONFIG?.apiBaseUrl;
  return fetch(`${apiBase}${path}`, {
    headers: {
      "x-correlation-id": correlationId
    }
  });
}

function initSwaTelemetry() {
  const cfg = window.HELLO_PORTAL_CONFIG || {};
  if (!cfg.appInsightsConnectionString || !window.Microsoft || !window.Microsoft.ApplicationInsights) {
    return null;
  }

  const appInsights = new window.Microsoft.ApplicationInsights.ApplicationInsights({
    config: {
      connectionString: cfg.appInsightsConnectionString
    }
  });
  appInsights.loadAppInsights();
  appInsights.addTelemetryInitializer((envelope) => {
    envelope.data = envelope.data || {};
    envelope.data.baseData = envelope.data.baseData || {};
    envelope.data.baseData.properties = envelope.data.baseData.properties || {};
    envelope.data.baseData.properties.surface = "swa";
    envelope.data.baseData.properties.correlationId = correlationId;
  });
  appInsights.trackPageView({ name: "observability" });
  return appInsights;
}

function renderTrendCharts(points) {
  const trendCanvas = document.getElementById("appInsightsTrendChart");
  const latencyCanvas = document.getElementById("appInsightsLatencyChart");
  if (!trendCanvas || !latencyCanvas) {
    return;
  }

  const labels = points.map((point) => point.timestamp ? new Date(point.timestamp).toLocaleTimeString() : "n/a");
  const requests = points.map((point) => Math.round(point.requests ?? 0));
  const errors = points.map((point) => Math.round(point.errors ?? 0));
  const avgLatency = points.map((point) => Math.round(point.avgDurationMs ?? 0));
  const p95Latency = points.map((point) => Math.round(point.p95DurationMs ?? 0));

  trendChart?.destroy();
  trendChart = new Chart(trendCanvas, {
    type: "line",
    data: {
      labels,
      datasets: [
        {
          label: "Requests",
          data: requests,
          borderColor: "#0078d4",
          backgroundColor: "rgba(0,120,212,0.15)",
          tension: 0.3
        },
        {
          label: "Errors",
          data: errors,
          borderColor: "#a80000",
          backgroundColor: "rgba(168,0,0,0.12)",
          tension: 0.3
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false
    }
  });

  latencyChart?.destroy();
  latencyChart = new Chart(latencyCanvas, {
    type: "bar",
    data: {
      labels,
      datasets: [
        {
          label: "Average latency (ms)",
          data: avgLatency,
          backgroundColor: "#107c10"
        },
        {
          label: "P95 latency (ms)",
          data: p95Latency,
          backgroundColor: "#ca5010"
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false
    }
  });
}

async function loadAppInsightsSummary() {
  const status = document.getElementById("status");
  const apiBase = window.HELLO_PORTAL_CONFIG?.apiBaseUrl;

  if (!status) {
    return [];
  }

  if (!apiBase) {
    status.className = "error";
    status.textContent = "Missing HELLO_PORTAL_CONFIG.apiBaseUrl";
    return [];
  }

  try {
    const response = await apiFetch("/observability/appinsights");
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    const points = Array.isArray(data.points) ? data.points : [];
    const latest = points.length > 0 ? points[points.length - 1] : null;

    document.getElementById("requestsValue").textContent = latest ? Math.round(latest.requests).toString() : "0";
    document.getElementById("errorsValue").textContent = latest ? Math.round(latest.errors).toString() : "0";
    document.getElementById("latencyValue").textContent = latest ? `${Math.round(latest.p95DurationMs)}ms` : "0ms";

    const sourceTs = data.latestSourceTimestampUtc || "n/a";
    status.textContent = `API checked at ${data.generatedAtUtc}; source timestamp=${sourceTs}`;
    renderTrendCharts(points);
    return points;
  } catch (error) {
    status.className = "error";
    status.textContent = `Failed to load App Insights data: ${error.message}`;
    return [];
  }
}

async function loadObservabilityDetails(appInsightsClient) {
  const status = document.getElementById("status");
  if (!status) {
    return;
  }

  try {
    const response = await apiFetch("/observability/details");
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    const functionExecutions = data.function?.executions ?? [];
    const functionTraces = data.function?.traces ?? [];
    const swaExecutions = data.swa?.executions ?? [];
    const swaTraces = data.swa?.traces ?? [];
    const correlations = data.correlations ?? [];

    renderRows("functionExecutionsRows", functionExecutions, ["timestamp", "name", "resultCode", "operation_Id"]);
    renderRows("functionTracesRows", functionTraces, ["timestamp", "severityLevel", "message"]);

    setPanelVisibility("swaExecutionsPanel", swaExecutions);
    setPanelVisibility("swaTracesPanel", swaTraces);
    setPanelVisibility("correlationsPanel", correlations);

    if (swaExecutions.length > 0) {
      renderRows("swaExecutionsRows", swaExecutions, ["timestamp", "name", "url", "operation_Id"]);
    }
    if (swaTraces.length > 0) {
      renderRows("swaTracesRows", swaTraces, ["timestamp", "severityLevel", "message"]);
    }
    if (correlations.length > 0) {
      renderRows("correlationRows", correlations, ["correlationId", "apiPath", "swaTimestamp", "functionTimestamp"]);
    }

    if (appInsightsClient) {
      appInsightsClient.trackEvent({
        name: "swa.api.call",
        properties: {
          surface: "swa",
          correlationId,
          page: "observability",
          apiPath: "/observability/details"
        }
      });
    }
  } catch (error) {
    status.className = "error";
    status.textContent = `Failed to load observability details: ${error.message}`;
  }
}

async function loadSwaSignals() {
  const host = document.getElementById("swaHost");
  const checked = document.getElementById("swaChecked");
  const date = document.getElementById("swaDate");
  const ref = document.getElementById("swaRef");
  const etag = document.getElementById("swaEtag");

  if (!host || !checked || !date || !ref || !etag) {
    return;
  }

  host.textContent = `hostname: ${window.location.hostname}`;
  checked.textContent = `checkedAtUtc: ${new Date().toISOString()}`;

  try {
    const response = await fetch("/index.html", { method: "HEAD", cache: "no-store" });
    date.textContent = `responseDateHeader: ${response.headers.get("date") || "n/a"}`;
    ref.textContent = `xAzureRefHeader: ${response.headers.get("x-azure-ref") || "n/a"}`;
    etag.textContent = `etagHeader: ${response.headers.get("etag") || "n/a"}`;
  } catch (error) {
    date.textContent = `responseDateHeader: unavailable (${error.message})`;
    ref.textContent = "xAzureRefHeader: unavailable";
    etag.textContent = "etagHeader: unavailable";
  }
}

const appInsightsClient = initSwaTelemetry();
await loadAppInsightsSummary();
await loadObservabilityDetails(appInsightsClient);
await loadSwaSignals();
