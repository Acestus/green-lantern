import Chart from "chart.js/auto";

let storageCountsChart;
let storageFreshnessChart;

function parseDateOrNull(value) {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

function renderCountsChart(data) {
  const ctx = document.getElementById("storageCountsChart");
  if (!ctx) {
    return;
  }

  const values = [
    Number(data?.blob?.count ?? 0),
    Number(data?.queue?.approximateMessages ?? 0),
    Number(data?.table?.latest?.length ?? 0)
  ];

  storageCountsChart?.destroy();
  storageCountsChart = new Chart(ctx, {
    type: "bar",
    data: {
      labels: ["Blob count", "Queue messages", "Recent table rows"],
      datasets: [
        {
          label: "Count",
          data: values,
          backgroundColor: ["#0078d4", "#107c10", "#ca5010"]
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false }
      }
    }
  });
}

function renderFreshnessChart(data) {
  const ctx = document.getElementById("storageFreshnessChart");
  if (!ctx) {
    return;
  }

  const now = new Date();
  const timestamps = data?.sourceTimestamps ?? {};
  const ageMinutes = [
    parseDateOrNull(timestamps.blobLastModifiedUtc),
    parseDateOrNull(timestamps.queueLastInsertedUtc),
    parseDateOrNull(timestamps.tableLastEntityTimestampUtc)
  ].map((value) => (value ? Math.max(0, Math.round((now.getTime() - value.getTime()) / 60000)) : null));

  storageFreshnessChart?.destroy();
  storageFreshnessChart = new Chart(ctx, {
    type: "doughnut",
    data: {
      labels: ["Blob age (min)", "Queue age (min)", "Table age (min)"],
      datasets: [
        {
          data: ageMinutes.map((value) => value ?? 0),
          backgroundColor: ["#2899f5", "#2aa743", "#d26b2d"]
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        tooltip: {
          callbacks: {
            label(context) {
              const rawValue = context.raw ?? 0;
              return `${context.label}: ${rawValue} min`;
            }
          }
        }
      }
    }
  });
}

function renderStorageTable(data) {
  const rows = document.getElementById("storageRows");
  if (!rows) {
    return;
  }

  const blobSample = (data?.blob?.sample ?? []).join(", ") || "none";
  const tableSample = (data?.table?.latest ?? []).map((x) => x.rowKey).join(", ") || "none";
  const blobTs = data?.sourceTimestamps?.blobLastModifiedUtc || "n/a";
  const queueTs = data?.sourceTimestamps?.queueLastInsertedUtc || "n/a";
  const tableTs = data?.sourceTimestamps?.tableLastEntityTimestampUtc || "n/a";

  rows.innerHTML = `
    <tr>
      <td>Blob</td>
      <td>${data?.blob?.container ?? "n/a"}</td>
      <td class="ok">Live</td>
      <td>count=${data?.blob?.count ?? 0}; sourceTimestamp=${blobTs}; sample=${blobSample}</td>
    </tr>
    <tr>
      <td>Queue</td>
      <td>${data?.queue?.name ?? "n/a"}</td>
      <td class="ok">Live</td>
      <td>approximateMessages=${data?.queue?.approximateMessages ?? 0}; sourceTimestamp=${queueTs}</td>
    </tr>
    <tr>
      <td>Table</td>
      <td>${data?.table?.name ?? "n/a"}</td>
      <td class="ok">Live</td>
      <td>sourceTimestamp=${tableTs}; recentRowKeys=${tableSample}</td>
    </tr>
  `;
}

async function loadStorageSummary() {
  const status = document.getElementById("status");
  const apiBase = window.HELLO_PORTAL_CONFIG?.apiBaseUrl;

  if (!status) {
    return;
  }

  if (!apiBase) {
    status.className = "error";
    status.textContent = "Missing HELLO_PORTAL_CONFIG.apiBaseUrl";
    return;
  }

  try {
    const response = await fetch(`${apiBase}/storage/summary`);
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    status.textContent = `API checked at ${data.generatedAtUtc}`;
    renderStorageTable(data);
    renderCountsChart(data);
    renderFreshnessChart(data);
  } catch (error) {
    status.className = "error";
    status.textContent = `Failed to load storage data: ${error.message}`;
  }
}

loadStorageSummary();

