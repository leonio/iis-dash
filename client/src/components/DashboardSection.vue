<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import {
  useApi,
  type ExportMode,
  type HeatmapCell,
  type HeatmapResponse,
  type LogEntryDto,
  type LogMetricsResponse,
  type LogQueryFilter,
} from '@/composables/useApi'

const { getLogMetrics, getHeatmap, getLogEntries, exportLogAnalytics } = useApi()

const filters = reactive({
  start: '',
  end: '',
  method: '',
  endpoint: '',
  statusGroup: 'all',
  topN: 10,
  includeOther: true,
})

const props = defineProps<{ section?: string }>()
const drilldown = ref<{ label: string; filter: Partial<LogQueryFilter> } | null>(null)
const activeSection = computed(
  () =>
    (props.section as
      | 'summary'
      | 'traffic'
      | 'status'
      | 'latency'
      | 'heatmap'
      | 'details'
      | undefined) ?? 'summary',
)

const metrics = ref<LogMetricsResponse | null>(null)
const heatmap = ref<HeatmapResponse | null>(null)
const entries = ref<LogEntryDto[]>([])
type SortKey =
  | 'timestamp'
  | 'method'
  | 'endpoint'
  | 'status'
  | 'latency'
  | 'clientIp'
  | 'user'
  | 'userAgent'
type SortDirection = 'asc' | 'desc'
const sortState = reactive<{ key: SortKey; direction: SortDirection }>({
  key: 'timestamp',
  direction: 'desc',
})
const totalCount = ref(0)
const page = ref(1)
const pageSize = ref(50)
const isLoading = ref(false)
const isExporting = ref(false)
const loadError = ref<string | null>(null)
const overlayVisible = ref(false)
let overlayTimer: number | undefined
let overlayStart = 0

const trafficBucketMinutes = 15

const totalPages = computed(() =>
  totalCount.value === 0 ? 1 : Math.ceil(totalCount.value / pageSize.value),
)

const heatmapMaxCount = computed(() => {
  if (!heatmap.value) {
    return 0
  }

  return heatmap.value.cells.reduce((max, cell) => Math.max(max, cell.count), 0)
})

const heatmapCells = computed(() => {
  if (!heatmap.value) {
    return new Map<string, HeatmapCell>()
  }

  return new Map(heatmap.value.cells.map((cell) => [`${cell.endpointIndex}-${cell.bucketIndex}`, cell]))
})

const trafficMaxRate = computed(() => {
  if (!metrics.value || metrics.value.traffic.length === 0) {
    return 1
  }

  return metrics.value.traffic.reduce(
    (max, point) => Math.max(max, point.requestsPerMinute),
    1,
  )
})

const sortedEntries = computed(() => {
  if (entries.value.length === 0) {
    return []
  }

  const direction = sortState.direction === 'asc' ? 1 : -1
  const getValue = (entry: LogEntryDto) => {
    switch (sortState.key) {
      case 'timestamp':
        return entry.timestamp ? new Date(entry.timestamp).getTime() : 0
      case 'method':
        return entry.method ?? ''
      case 'endpoint':
        return entry.normalizedEndpoint ?? ''
      case 'status':
        return entry.protocolStatus ?? -1
      case 'latency':
        return entry.timeTakenMs ?? -1
      case 'clientIp':
        return entry.clientIp ?? ''
      case 'user':
        return entry.username ?? ''
      case 'userAgent':
        return entry.userAgent ?? ''
      default:
        return ''
    }
  }

  return [...entries.value].sort((a, b) => {
    const left = getValue(a)
    const right = getValue(b)

    if (typeof left === 'number' && typeof right === 'number') {
      return (left - right) * direction
    }

    return String(left).localeCompare(String(right), undefined, { numeric: true }) * direction
  })
})

const summaryCards = computed(() => {
  if (!metrics.value) {
    return []
  }

  const latency = metrics.value.latency
  const errorRate = `${(metrics.value.errorRate * 100).toFixed(2)}%`

  return [
    { label: 'Total requests', value: metrics.value.totalRequests.toLocaleString() },
    { label: 'Error rate', value: errorRate },
    { label: 'P50 latency (ms)', value: latency.p50Ms?.toLocaleString() ?? '—' },
    { label: 'P95 latency (ms)', value: latency.p95Ms?.toLocaleString() ?? '—' },
    { label: 'P99 latency (ms)', value: latency.p99Ms?.toLocaleString() ?? '—' },
  ]
})

const formatIsoInput = (value: string) => (value ? new Date(value).toISOString() : null)

const buildFilter = (): LogQueryFilter => {
  const filter: LogQueryFilter = {
    start: formatIsoInput(filters.start),
    end: formatIsoInput(filters.end),
    method: filters.method || null,
    endpoint: filters.endpoint || null,
    statusGroup: filters.statusGroup === 'all' ? null : Number(filters.statusGroup),
  }

  if (drilldown.value) {
    Object.assign(filter, drilldown.value.filter)
  }

  return filter
}

const loadAll = async () => {
  isLoading.value = true
  loadError.value = null

  const filter = buildFilter()

  try {
    const [metricsResponse, heatmapResponse, entriesResponse] = await Promise.all([
      getLogMetrics({ filter, trafficBucketMinutes }),
      getHeatmap({ filter, topN: filters.topN, includeOther: filters.includeOther }),
      getLogEntries({ filter, page: page.value, pageSize: pageSize.value }),
    ])

    metrics.value = metricsResponse
    heatmap.value = heatmapResponse
    entries.value = entriesResponse.items
    totalCount.value = entriesResponse.totalCount
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : 'Failed to load analytics.'
  } finally {
    isLoading.value = false
  }
}

const applyFilters = async () => {
  page.value = 1
  await loadAll()
}

const clearDrilldown = async () => {
  drilldown.value = null
  await applyFilters()
}

const setDrilldown = async (label: string, filter: Partial<LogQueryFilter>) => {
  drilldown.value = { label, filter }
  page.value = 1
  await loadAll()
}

const handleTrafficClick = async (bucketStart: string, count: number) => {
  const start = bucketStart
  const end = new Date(new Date(bucketStart).getTime() + trafficBucketMinutes * 60 * 1000).toISOString()
  await setDrilldown(
    `Traffic ${new Date(bucketStart).toLocaleString()} (${count.toLocaleString()} reqs)`,
    { start, end },
  )
}

const handleStatusClick = async (group: string) => {
  const statusGroup = Number(group.replace('xx', ''))
  if (Number.isNaN(statusGroup)) {
    return
  }

  await setDrilldown(`Status ${group}`, { statusGroup })
}

const handleHeatmapClick = async (endpoint: string, bucketIndex: number) => {
  if (!heatmap.value) {
    return
  }

  const startMinutes = bucketIndex * heatmap.value.bucketMinutes
  const endMinutes = startMinutes + heatmap.value.bucketMinutes

  await setDrilldown(`Heatmap ${endpoint} @ ${heatmap.value.buckets[bucketIndex]}`,
    {
      endpoint,
      timeOfDayStartMinutes: startMinutes,
      timeOfDayEndMinutes: endMinutes,
    },
  )
}

const exportChart = async (mode: ExportMode) => {
  isExporting.value = true
  const filter = buildFilter()
  try {
    const { blob, fileName } = await exportLogAnalytics({
      filter,
      topN: filters.topN,
      includeOther: filters.includeOther,
      mode,
    })

    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    link.click()
    URL.revokeObjectURL(url)
  } finally {
    isExporting.value = false
  }
}

const prevPage = async () => {
  if (page.value <= 1) {
    return
  }

  page.value -= 1
  await loadAll()
}

const nextPage = async () => {
  if (page.value >= totalPages.value) {
    return
  }

  page.value += 1
  await loadAll()
}

const formatDuration = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : `${value.toLocaleString()} ms`

const toggleSort = (key: SortKey) => {
  if (sortState.key === key) {
    sortState.direction = sortState.direction === 'asc' ? 'desc' : 'asc'
    return
  }

  sortState.key = key
  sortState.direction = key === 'timestamp' ? 'desc' : 'asc'
}

const sortIndicator = (key: SortKey) => {
  if (sortState.key !== key) {
    return '↕'
  }

  return sortState.direction === 'asc' ? '▲' : '▼'
}

const sortAria = (key: SortKey) => {
  if (sortState.key !== key) {
    return 'none'
  }

  return sortState.direction === 'asc' ? 'ascending' : 'descending'
}

const isBusy = computed(() => isLoading.value || isExporting.value)

const clearOverlayTimer = () => {
  if (overlayTimer !== undefined) {
    window.clearTimeout(overlayTimer)
    overlayTimer = undefined
  }
}

watch(isBusy, (busy) => {
  if (busy) {
    clearOverlayTimer()
    overlayStart = Date.now()
    overlayVisible.value = true
    return
  }

  const elapsed = Date.now() - overlayStart
  const remaining = Math.max(0, 300 - elapsed)
  clearOverlayTimer()
  if (remaining === 0) {
    overlayVisible.value = false
    return
  }

  overlayTimer = window.setTimeout(() => {
    overlayVisible.value = false
    overlayTimer = undefined
  }, remaining)
})

onUnmounted(() => {
  clearOverlayTimer()
})

onMounted(() => {
  void loadAll()
})
</script>

<template>
  <section class="space-y-6">
    <div
      v-if="overlayVisible"
      class="fixed inset-0 z-50 flex items-center justify-center bg-background/60 backdrop-blur-sm"
    >
      <div class="flex items-center gap-3 rounded-md border border-muted-foreground/20 bg-card px-4 py-3 text-sm text-foreground shadow">
        <span class="inline-flex h-4 w-4 animate-spin rounded-full border-2 border-muted-foreground/40 border-t-primary" />
        Loading…
      </div>
    </div>
    <div class="grid items-start gap-6 md:grid-cols-[minmax(0,1fr)_340px]">
      <div class="space-y-6">
        <Card>
          <CardHeader>
            <div class="flex flex-col gap-3">
              <div>
                <CardTitle>Usage analytics</CardTitle>
                <CardDescription>Choose a view and drill down into the data.</CardDescription>
              </div>
              <div class="flex flex-wrap gap-2">
                <RouterLink to="/dashboard/summary" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'summary' ? 'border-primary text-primary' : ''"
                  >
                    Summary
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/traffic" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'traffic' ? 'border-primary text-primary' : ''"
                  >
                    Traffic
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/status" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'status' ? 'border-primary text-primary' : ''"
                  >
                    Status
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/latency" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'latency' ? 'border-primary text-primary' : ''"
                  >
                    Latency
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/heatmap" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'heatmap' ? 'border-primary text-primary' : ''"
                  >
                    Heatmap
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/details" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'details' ? 'border-primary text-primary' : ''"
                  >
                    Details
                  </Button>
                </RouterLink>
              </div>
            </div>
          </CardHeader>
        </Card>

        <Card v-if="drilldown">
          <CardContent class="rounded-md border border-primary/30 bg-primary/5 p-3 text-sm">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <span class="font-medium text-foreground">Drill-down:</span>
              <span class="text-muted-foreground">{{ drilldown.label }}</span>
              <Button type="button" variant="outline" size="sm" @click="clearDrilldown">
                Clear drill-down
              </Button>
            </div>
          </CardContent>
        </Card>

        <div v-if="loadError" class="text-sm text-destructive">{{ loadError }}</div>

        <Card v-if="activeSection === 'summary'">
          <CardHeader>
            <CardTitle>Summary</CardTitle>
            <CardDescription>Overall metrics for the current filter.</CardDescription>
          </CardHeader>
          <CardContent>
            <div v-if="summaryCards.length" class="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
              <div
                v-for="card in summaryCards"
                :key="card.label"
                class="rounded-md border border-muted-foreground/20 p-3"
              >
                <div class="text-xs text-muted-foreground">{{ card.label }}</div>
                <div class="text-lg font-semibold text-foreground">{{ card.value }}</div>
              </div>
            </div>
            <div v-else class="text-sm text-muted-foreground">No summary data yet.</div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'traffic'">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <div>
                <CardTitle>Traffic (requests/min)</CardTitle>
                <CardDescription>{{ trafficBucketMinutes }}-minute buckets</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Traffic')">
                {{ isExporting ? 'Exporting…' : 'Export' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!metrics" class="text-sm text-muted-foreground">No traffic data.</div>
            <div v-else class="space-y-2">
              <div class="text-xs text-muted-foreground">
                Click a row to drill down by time.
              </div>
              <div class="space-y-1">
                <div
                  v-for="point in metrics.traffic"
                  :key="point.bucketStart"
                  class="flex items-center gap-2 text-xs"
                >
                  <button
                    type="button"
                    class="flex-1 rounded-sm bg-muted/30 p-1 text-left hover:bg-muted/50"
                    @click="handleTrafficClick(point.bucketStart, point.count)"
                  >
                    <div
                      class="h-2 rounded-sm bg-primary/70"
                      :style="{ width: `${Math.min(100, (point.requestsPerMinute / trafficMaxRate) * 100)}%` }"
                    />
                  </button>
                  <span class="w-32 text-muted-foreground">{{ new Date(point.bucketStart).toLocaleString() }}</span>
                  <span class="w-16 text-right text-foreground">{{ point.requestsPerMinute.toFixed(2) }}</span>
                  <span class="w-14 text-right text-muted-foreground">{{ point.count }}</span>
                </div>
              </div>

              <div v-if="drilldown" class="space-y-2 pt-4">
                <div class="text-sm font-medium text-foreground">Row details</div>
                <div class="overflow-x-auto rounded-md border border-muted-foreground/20">
                  <table class="min-w-full text-sm">
                    <thead class="bg-muted/30 text-left text-xs uppercase text-muted-foreground">
                      <tr>
                        <th class="px-3 py-2" :aria-sort="sortAria('timestamp')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('timestamp')"
                          >
                            Timestamp
                            <span class="text-[10px]">{{ sortIndicator('timestamp') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('method')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('method')"
                          >
                            Method
                            <span class="text-[10px]">{{ sortIndicator('method') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('endpoint')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('endpoint')"
                          >
                            Endpoint
                            <span class="text-[10px]">{{ sortIndicator('endpoint') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('status')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('status')"
                          >
                            Status
                            <span class="text-[10px]">{{ sortIndicator('status') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('latency')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('latency')"
                          >
                            Latency
                            <span class="text-[10px]">{{ sortIndicator('latency') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('clientIp')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('clientIp')"
                          >
                            Client IP
                            <span class="text-[10px]">{{ sortIndicator('clientIp') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('user')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('user')"
                          >
                            User
                            <span class="text-[10px]">{{ sortIndicator('user') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('userAgent')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('userAgent')"
                          >
                            User Agent
                            <span class="text-[10px]">{{ sortIndicator('userAgent') }}</span>
                          </button>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-if="entries.length === 0">
                        <td colspan="8" class="px-3 py-4 text-center text-sm text-muted-foreground">
                          No entries for the selected row.
                        </td>
                      </tr>
                      <tr v-for="entry in sortedEntries" :key="entry.id" class="border-t border-muted-foreground/10">
                        <td class="px-3 py-2 text-muted-foreground">
                          {{ entry.timestamp ? new Date(entry.timestamp).toLocaleString() : '—' }}
                        </td>
                        <td class="px-3 py-2 text-foreground">{{ entry.method ?? '—' }}</td>
                        <td class="px-3 py-2 text-foreground">{{ entry.normalizedEndpoint }}</td>
                        <td class="px-3 py-2 text-foreground">
                          {{ entry.statusGroup }} ({{ entry.protocolStatus ?? '—' }})
                        </td>
                        <td class="px-3 py-2 text-foreground">{{ formatDuration(entry.timeTakenMs) }}</td>
                        <td class="px-3 py-2 text-foreground">{{ entry.clientIp ?? '—' }}</td>
                        <td class="px-3 py-2 text-foreground">{{ entry.username ?? '—' }}</td>
                        <td class="px-3 py-2 text-foreground">{{ entry.userAgent ?? '—' }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'status'">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <div>
                <CardTitle>Status distribution</CardTitle>
                <CardDescription>Grouped by 2xx/3xx/4xx/5xx.</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Status')">
                {{ isExporting ? 'Exporting…' : 'Export' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!metrics" class="text-sm text-muted-foreground">No status data.</div>
            <div v-else class="space-y-2">
              <div class="text-xs text-muted-foreground">Click a group to drill down.</div>
              <div class="space-y-2">
                <button
                  v-for="group in metrics.statusGroups"
                  :key="group.group"
                  type="button"
                  class="flex w-full items-center gap-3 rounded-md border border-muted-foreground/20 px-3 py-2 text-left hover:bg-muted/10"
                  :disabled="group.group === 'Other'"
                  @click="handleStatusClick(group.group)"
                >
                  <span class="w-10 text-sm font-medium text-foreground">{{ group.group }}</span>
                  <div class="flex-1 rounded-sm bg-muted/30">
                    <div
                      class="h-2 rounded-sm bg-primary/70"
                      :style="{ width: `${(group.count / Math.max(1, metrics.totalRequests)) * 100}%` }"
                    />
                  </div>
                  <span class="w-16 text-right text-sm text-foreground">{{ group.count }}</span>
                </button>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'latency'">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <div>
                <CardTitle>Latency percentiles</CardTitle>
                <CardDescription>Average and $p$-values for the selected range.</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Latency')">
                {{ isExporting ? 'Exporting…' : 'Export' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!metrics" class="text-sm text-muted-foreground">No latency data.</div>
            <div v-else class="grid gap-3 sm:grid-cols-2">
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">Average</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.averageMs ?? null) }}
                </div>
              </div>
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">P50</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.p50Ms ?? null) }}
                </div>
              </div>
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">P95</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.p95Ms ?? null) }}
                </div>
              </div>
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">P99</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.p99Ms ?? null) }}
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'heatmap'">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <div>
                <CardTitle>Endpoint usage heatmap</CardTitle>
                <CardDescription>15-minute buckets by time-of-day.</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Heatmap')">
                {{ isExporting ? 'Exporting…' : 'Export' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!heatmap" class="text-sm text-muted-foreground">No heatmap data.</div>
            <div v-else class="space-y-2">
              <div class="text-xs text-muted-foreground">Click a cell to drill down.</div>
              <div class="overflow-x-auto">
                <div class="min-w-[900px]">
                  <div
                    class="grid"
                    :style="{ gridTemplateColumns: `200px repeat(${heatmap.buckets.length}, minmax(16px, 1fr))` }"
                  >
                    <div class="sticky left-0 z-10 bg-muted/30 px-2 py-2 text-sm font-semibold text-foreground">Endpoint</div>
                    <div
                      v-for="bucket in heatmap.buckets"
                      :key="bucket"
                      class="flex h-12 items-end justify-center text-[10px] text-muted-foreground [writing-mode:vertical-rl] rotate-180"
                    >
                      {{ bucket }}
                    </div>
                    <template v-for="(endpoint, endpointIndex) in heatmap.endpoints" :key="endpoint">
                        <div class="sticky left-0 bg-background px-2 py-1 text-xs font-medium text-foreground">
                          {{ endpoint }}
                        </div>
                      <button
                        v-for="bucketIndex in heatmap.buckets.length"
                        :key="`${endpoint}-${bucketIndex}`"
                        type="button"
                        class="h-6 border border-muted-foreground/10"
                        :style="{
                          backgroundColor: `rgba(59, 130, 246, ${
                            heatmapCells.get(`${endpointIndex}-${bucketIndex - 1}`)?.count
                              ? (heatmapCells.get(`${endpointIndex}-${bucketIndex - 1}`)?.count ?? 0) /
                                Math.max(1, heatmapMaxCount)
                              : 0
                          })`,
                        }"
                        @click="handleHeatmapClick(endpoint, bucketIndex - 1)"
                      />
                    </template>
                  </div>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'details'">
          <CardHeader>
            <div class="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
              <div>
                <CardTitle>Detail panel</CardTitle>
                <CardDescription>Summary metrics and raw entries.</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Entries')">
                {{ isExporting ? 'Exporting…' : 'Export entries' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent class="space-y-4">
            <div v-if="summaryCards.length" class="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
              <div
                v-for="card in summaryCards"
                :key="card.label"
                class="rounded-md border border-muted-foreground/20 p-3"
              >
                <div class="text-xs text-muted-foreground">{{ card.label }}</div>
                <div class="text-lg font-semibold text-foreground">{{ card.value }}</div>
              </div>
            </div>

            <div class="overflow-x-auto rounded-md border border-muted-foreground/20">
              <table class="min-w-full text-sm">
                <thead class="bg-muted/30 text-left text-xs uppercase text-muted-foreground">
                  <tr>
                    <th class="px-3 py-2" :aria-sort="sortAria('timestamp')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('timestamp')"
                      >
                        Timestamp
                        <span class="text-[10px]">{{ sortIndicator('timestamp') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('method')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('method')"
                      >
                        Method
                        <span class="text-[10px]">{{ sortIndicator('method') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('endpoint')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('endpoint')"
                      >
                        Endpoint
                        <span class="text-[10px]">{{ sortIndicator('endpoint') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('status')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('status')"
                      >
                        Status
                        <span class="text-[10px]">{{ sortIndicator('status') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('latency')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('latency')"
                      >
                        Latency
                        <span class="text-[10px]">{{ sortIndicator('latency') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('clientIp')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('clientIp')"
                      >
                        Client IP
                        <span class="text-[10px]">{{ sortIndicator('clientIp') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('user')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('user')"
                      >
                        User
                        <span class="text-[10px]">{{ sortIndicator('user') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('userAgent')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('userAgent')"
                      >
                        User Agent
                        <span class="text-[10px]">{{ sortIndicator('userAgent') }}</span>
                      </button>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-if="entries.length === 0">
                    <td colspan="8" class="px-3 py-4 text-center text-sm text-muted-foreground">
                      No entries for the selected filters.
                    </td>
                  </tr>
                  <tr v-for="entry in sortedEntries" :key="entry.id" class="border-t border-muted-foreground/10">
                    <td class="px-3 py-2 text-muted-foreground">
                      {{ entry.timestamp ? new Date(entry.timestamp).toLocaleString() : '—' }}
                    </td>
                    <td class="px-3 py-2 text-foreground">{{ entry.method ?? '—' }}</td>
                    <td class="px-3 py-2 text-foreground">{{ entry.normalizedEndpoint }}</td>
                    <td class="px-3 py-2 text-foreground">
                      {{ entry.statusGroup }} ({{ entry.protocolStatus ?? '—' }})
                    </td>
                    <td class="px-3 py-2 text-foreground">{{ formatDuration(entry.timeTakenMs) }}</td>
                    <td class="px-3 py-2 text-foreground">{{ entry.clientIp ?? '—' }}</td>
                    <td class="px-3 py-2 text-foreground">{{ entry.username ?? '—' }}</td>
                    <td class="px-3 py-2 text-foreground">{{ entry.userAgent ?? '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
              <div class="text-muted-foreground">
                Page {{ page }} of {{ totalPages }} ({{ totalCount.toLocaleString() }} entries)
              </div>
              <div class="flex items-center gap-2">
                <select
                  v-model.number="pageSize"
                  class="rounded-md border border-muted-foreground/40 bg-background px-2 py-1 text-sm"
                  @change="applyFilters"
                >
                  <option :value="25">25</option>
                  <option :value="50">50</option>
                  <option :value="100">100</option>
                </select>
                <Button type="button" variant="outline" size="sm" :disabled="page <= 1" @click="prevPage">
                  Prev
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  :disabled="page >= totalPages"
                  @click="nextPage"
                >
                  Next
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="space-y-6 md:sticky md:top-6">
        <Card>
          <CardHeader>
            <CardTitle>Filters</CardTitle>
            <CardDescription>Refine analytics results.</CardDescription>
          </CardHeader>
          <CardContent class="grid gap-4">
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-start">Start</label>
              <input
                id="filter-start"
                v-model="filters.start"
                type="datetime-local"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-end">End</label>
              <input
                id="filter-end"
                v-model="filters.end"
                type="datetime-local"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-method">Method</label>
              <input
                id="filter-method"
                v-model="filters.method"
                placeholder="GET, POST"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-endpoint">Endpoint</label>
              <input
                id="filter-endpoint"
                v-model="filters.endpoint"
                placeholder="/api/orders/:id"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-status">Status group</label>
              <select
                id="filter-status"
                v-model="filters.statusGroup"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              >
                <option value="all">All</option>
                <option value="2">2xx</option>
                <option value="3">3xx</option>
                <option value="4">4xx</option>
                <option value="5">5xx</option>
              </select>
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-topn">Top endpoints</label>
              <select
                id="filter-topn"
                v-model.number="filters.topN"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              >
                <option :value="5">Top 5</option>
                <option :value="10">Top 10</option>
                <option :value="20">Top 20</option>
                <option :value="30">Top 30</option>
              </select>
              <label class="flex items-center gap-2 text-sm text-muted-foreground">
                <input v-model="filters.includeOther" type="checkbox" class="h-4 w-4" />
                Include Other bucket
              </label>
            </div>
            <div class="flex flex-col gap-2">
              <Button type="button" :disabled="isLoading" @click="applyFilters">
                {{ isLoading ? 'Refreshing…' : 'Apply filters' }}
              </Button>
              <Button type="button" variant="outline" :disabled="isExporting" @click="exportChart('All')">
                {{ isExporting ? 'Exporting…' : 'Export all charts' }}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </section>
</template>
