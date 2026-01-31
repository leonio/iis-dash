<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
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

const { t, n, d } = useI18n()
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
  const errorRate = n(metrics.value.errorRate, 'percent')

  return [
    { label: t('summary.cards.totalRequests'), value: n(metrics.value.totalRequests) },
    { label: t('summary.cards.errorRate'), value: n(metrics.value.errorRate) },
    { label: t('summary.cards.p50'), value: (latency.p50Ms != null) ? n(latency.p50Ms) : '—' },
    { label: t('summary.cards.p95'), value: (latency.p95Ms != null) ? n(latency.p95Ms) : '—' },
    { label: t('summary.cards.p99'), value: (latency.p99Ms != null) ? n(latency.p99Ms) : '—' },
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
    loadError.value = error instanceof Error ? error.message : t('errors.loadFailed')
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
    t('drilldown.traffic', { time: d(new Date(bucketStart), 'long'), count: n(count) }),
    { start, end },
  )
}

const handleStatusClick = async (group: string) => {
  const statusGroup = Number(group.replace('xx', ''))
  if (Number.isNaN(statusGroup)) {
    return
  }

  await setDrilldown(t('drilldown.status', { group }), { statusGroup })
}

const handleHeatmapClick = async (endpoint: string, bucketIndex: number) => {
  if (!heatmap.value) {
    return
  }

  const startMinutes = bucketIndex * heatmap.value.bucketMinutes
  const endMinutes = startMinutes + heatmap.value.bucketMinutes

  await setDrilldown(t('drilldown.heatmap', { endpoint, bucket: heatmap.value.buckets[bucketIndex] }),
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
  value === null || value === undefined ? '—' : t('latency_ms', { value: n(value, 'decimal') })

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
        {{ t('status.loading') }}
      </div>
    </div>
    <div class="grid items-start gap-6 md:grid-cols-[minmax(0,1fr)_340px]">
      <div class="space-y-6 pb-20">
        <Card class="bg-primary/5 border-primary/10">
          <CardHeader class="py-3">
            <CardTitle class="text-sm font-medium">{{ t('dashboard_usage.title') }}</CardTitle>
          </CardHeader>
          <CardContent class="pb-3 text-xs text-muted-foreground leading-relaxed">
            {{ t(`dashboard_usage.${activeSection}`) }}
          </CardContent>
        </Card>

        <Card class="sticky top-0 z-20 border-b-primary/10 bg-card/95 backdrop-blur shadow-sm">
          <CardHeader class="py-4">
            <div class="flex flex-col gap-3">
              <div>
                <CardTitle>{{ t('nav.title') }}</CardTitle>
                <CardDescription class="hidden sm:block">{{ t('nav.subtitle') }}</CardDescription>
              </div>
              <div class="flex flex-wrap gap-2">
                <RouterLink to="/dashboard/summary" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'summary' ? 'border-primary text-primary' : ''"
                  >
                    {{ t('sections.summary') }}
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/traffic" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'traffic' ? 'border-primary text-primary' : ''"
                  >
                    {{ t('sections.traffic') }}
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/status" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'status' ? 'border-primary text-primary' : ''"
                  >
                    {{ t('sections.status') }}
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/latency" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'latency' ? 'border-primary text-primary' : ''"
                  >
                    {{ t('sections.latency') }}
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/heatmap" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'heatmap' ? 'border-primary text-primary' : ''"
                  >
                    {{ t('sections.heatmap') }}
                  </Button>
                </RouterLink>
                <RouterLink to="/dashboard/details" class="no-underline">
                  <Button
                    type="button"
                    variant="outline"
                    :class="activeSection === 'details' ? 'border-primary text-primary' : ''"
                  >
                    {{ t('sections.details') }}
                  </Button>
                </RouterLink>
              </div>
            </div>
          </CardHeader>
        </Card>

        <Card v-if="drilldown">
          <CardContent class="rounded-md border border-primary/30 bg-primary/5 p-3 text-sm">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <span class="font-medium text-foreground">{{ t('drilldown.label') }}</span>
              <span class="text-muted-foreground">{{ drilldown.label }}</span>
              <Button type="button" variant="outline" size="sm" @click="clearDrilldown">
                {{ t('drilldown.clear') }}
              </Button>
            </div>
          </CardContent>
        </Card>

        <div v-if="loadError" class="text-sm text-destructive">{{ loadError }}</div>

        <Card v-if="activeSection === 'summary'">
          <CardHeader>
            <CardTitle>{{ t('summary.title') }}</CardTitle>
            <CardDescription>{{ t('summary.subtitle') }}</CardDescription>
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
            <div v-else class="text-sm text-muted-foreground">{{ t('summary.empty') }}</div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'traffic'">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <div>
                <CardTitle>{{ t('traffic.title') }}</CardTitle>
                <CardDescription>{{ t('traffic.subtitle', { minutes: trafficBucketMinutes }) }}</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Traffic')">
                {{ isExporting ? t('buttons.exporting') : t('buttons.export') }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!metrics" class="text-sm text-muted-foreground">{{ t('traffic.empty') }}</div>
            <div v-else class="space-y-2">
              <div class="text-xs text-muted-foreground">
                {{ t('traffic.instruction') }}
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
                  <span class="w-32 text-muted-foreground">{{ d(new Date(point.bucketStart), 'long') }}</span>
                  <span class="w-16 text-right text-foreground">{{ n(point.requestsPerMinute, 'decimal') }}</span>
                  <span class="w-14 text-right text-muted-foreground">{{ n(point.count) }}</span>
                </div>
              </div>

              <div v-if="drilldown" class="space-y-2 pt-4">
                <div class="text-sm font-medium text-foreground">{{ t('drilldown.detailsTitle') }}</div>
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
                            {{ t('table.timestamp') }}
                            <span class="text-[10px]">{{ sortIndicator('timestamp') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('method')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('method')"
                          >
                            {{ t('table.method') }}
                            <span class="text-[10px]">{{ sortIndicator('method') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('endpoint')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('endpoint')"
                          >
                            {{ t('table.endpoint') }}
                            <span class="text-[10px]">{{ sortIndicator('endpoint') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('latency')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('latency')"
                          >
                            {{ t('table.latency') }}
                            <span class="text-[10px]">{{ sortIndicator('latency') }}</span>
                          </button>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-if="entries.length === 0">
                        <td colspan="4" class="px-3 py-4 text-center text-sm text-muted-foreground">
                          {{ t('traffic.rowEmpty') }}
                        </td>
                      </tr>
                      <tr v-for="entry in sortedEntries" :key="entry.id" class="border-t border-muted-foreground/10">
                        <td class="px-3 py-2 text-muted-foreground">
                          {{ entry.timestamp ? d(new Date(entry.timestamp), 'long') : '—' }}
                        </td>
                        <td class="px-3 py-2 text-foreground">{{ entry.method ?? '—' }}</td>
                        <td class="px-3 py-2 text-foreground">{{ entry.normalizedEndpoint }}</td>
                        <td class="px-3 py-2 text-foreground">{{ formatDuration(entry.timeTakenMs) }}</td>
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
                <CardTitle>{{ t('status_dist.title') }}</CardTitle>
                <CardDescription>{{ t('status_dist.subtitle') }}</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Status')">
                {{ isExporting ? t('buttons.exporting') : t('buttons.export') }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!metrics" class="text-sm text-muted-foreground">{{ t('status_dist.empty') }}</div>
            <div v-else class="space-y-2">
              <div class="text-xs text-muted-foreground">{{ t('status_dist.instruction') }}</div>
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
                  <span class="w-16 text-right text-sm text-foreground">{{ n(group.count) }}</span>
                </button>
              </div>

              <div v-if="drilldown" class="space-y-2 pt-4">
                <div class="text-sm font-medium text-foreground">{{ t('drilldown.detailsTitle') }}</div>
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
                            {{ t('table.timestamp') }}
                            <span class="text-[10px]">{{ sortIndicator('timestamp') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('method')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('method')"
                          >
                            {{ t('table.method') }}
                            <span class="text-[10px]">{{ sortIndicator('method') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('status')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('status')"
                          >
                            {{ t('table.status') }}
                            <span class="text-[10px]">{{ sortIndicator('status') }}</span>
                          </button>
                        </th>
                        <th class="px-3 py-2" :aria-sort="sortAria('endpoint')">
                          <button
                            type="button"
                            class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                            @click="toggleSort('endpoint')"
                          >
                            {{ t('table.endpoint') }}
                            <span class="text-[10px]">{{ sortIndicator('endpoint') }}</span>
                          </button>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-if="entries.length === 0">
                        <td colspan="4" class="px-3 py-4 text-center text-sm text-muted-foreground">
                          {{ t('status_dist.rowEmpty') }}
                        </td>
                      </tr>
                      <tr v-for="entry in sortedEntries" :key="entry.id" class="border-t border-muted-foreground/10">
                        <td class="px-3 py-2 text-muted-foreground">
                          {{ entry.timestamp ? d(new Date(entry.timestamp), 'long') : '—' }}
                        </td>
                        <td class="px-3 py-2 text-foreground">{{ entry.method ?? '—' }}</td>
                        <td class="px-3 py-2 text-foreground">
                          {{ entry.statusGroup }} ({{ entry.protocolStatus ?? '—' }})
                        </td>
                        <td class="px-3 py-2 text-foreground">{{ entry.normalizedEndpoint }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card v-if="activeSection === 'latency'">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <div>
                <CardTitle>{{ t('latency_percent.title') }}</CardTitle>
                <CardDescription>{{ t('latency_percent.subtitle') }}</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Latency')">
                {{ isExporting ? t('buttons.exporting') : t('buttons.export') }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="isLoading && !metrics" class="flex items-center justify-center py-8">
              <span class="h-6 w-6 animate-spin rounded-full border-2 border-muted-foreground/20 border-t-primary" />
            </div>
            <div v-else-if="!metrics || metrics.latency.sampleCount === 0" class="py-8 text-center text-sm text-muted-foreground">
              {{ t('latency_percent.empty') }}
            </div>
            <div v-else class="grid gap-3 sm:grid-cols-2">
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">{{ t('latency_percent.average') }}</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.averageMs ?? null) }}
                </div>
              </div>
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">{{ t('latency_percent.p50') }}</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.p50Ms ?? null) }}
                </div>
              </div>
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">{{ t('latency_percent.p95') }}</div>
                <div class="text-lg font-semibold text-foreground">
                  {{ formatDuration(metrics.latency.p95Ms ?? null) }}
                </div>
              </div>
              <div class="rounded-md border border-muted-foreground/20 p-3 text-sm">
                <div class="text-muted-foreground">{{ t('latency_percent.p99') }}</div>
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
                <CardTitle>{{ t('heatmap.title') }}</CardTitle>
                <CardDescription>{{ t('heatmap.subtitle') }}</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Heatmap')">
                {{ isExporting ? t('buttons.exporting') : t('buttons.export') }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div v-if="!heatmap" class="text-sm text-muted-foreground">{{ t('heatmap.empty') }}</div>
            <div v-else class="space-y-2">
              <div class="text-xs text-muted-foreground">{{ t('heatmap.instruction') }}</div>
              <div class="overflow-x-auto">
                <div class="min-w-[900px]">
                  <div
                    class="grid"
                    :style="{ gridTemplateColumns: `200px repeat(${heatmap.buckets.length}, minmax(16px, 1fr))` }"
                  >
                    <div class="sticky left-0 z-10 bg-muted/30 px-2 py-2 text-sm font-semibold text-foreground">{{ t('table.endpoint') }}</div>
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
                <CardTitle>{{ t('details.title') }}</CardTitle>
                <CardDescription>{{ t('details.subtitle') }}</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" :disabled="isExporting" @click="exportChart('Entries')">
                {{ isExporting ? t('buttons.exporting') : t('buttons.exportEntries') }}
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
                        {{ t('table.timestamp') }}
                        <span class="text-[10px]">{{ sortIndicator('timestamp') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('method')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('method')"
                      >
                        {{ t('table.method') }}
                        <span class="text-[10px]">{{ sortIndicator('method') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('endpoint')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('endpoint')"
                      >
                        {{ t('table.endpoint') }}
                        <span class="text-[10px]">{{ sortIndicator('endpoint') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('status')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('status')"
                      >
                        {{ t('table.status') }}
                        <span class="text-[10px]">{{ sortIndicator('status') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('latency')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('latency')"
                      >
                        {{ t('table.latency') }}
                        <span class="text-[10px]">{{ sortIndicator('latency') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('clientIp')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('clientIp')"
                      >
                        {{ t('table.clientIp') }}
                        <span class="text-[10px]">{{ sortIndicator('clientIp') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('user')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('user')"
                      >
                        {{ t('table.user') }}
                        <span class="text-[10px]">{{ sortIndicator('user') }}</span>
                      </button>
                    </th>
                    <th class="px-3 py-2" :aria-sort="sortAria('userAgent')">
                      <button
                        type="button"
                        class="inline-flex items-center gap-1 text-muted-foreground hover:text-foreground"
                        @click="toggleSort('userAgent')"
                      >
                        {{ t('table.userAgent') }}
                        <span class="text-[10px]">{{ sortIndicator('userAgent') }}</span>
                      </button>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-if="entries.length === 0">
                    <td colspan="8" class="px-3 py-4 text-center text-sm text-muted-foreground">
                      {{ t('details.empty') }}
                    </td>
                  </tr>
                  <tr v-for="entry in sortedEntries" :key="entry.id" class="border-t border-muted-foreground/10">
                    <td class="px-3 py-2 text-muted-foreground">
                      {{ entry.timestamp ? d(new Date(entry.timestamp), 'long') : '—' }}
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
                {{ t('table.pagination', { page, totalPages, count: n(totalCount) }) }}
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
                  {{ t('buttons.prev') }}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  :disabled="page >= totalPages"
                  @click="nextPage"
                >
                  {{ t('buttons.next') }}
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="space-y-6 md:sticky md:top-6">
        <Card>
          <CardHeader>
            <CardTitle>{{ t('filters.title') }}</CardTitle>
            <CardDescription>{{ t('filters.subtitle') }}</CardDescription>
          </CardHeader>
          <CardContent class="grid gap-4">
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-start">{{ t('filters.start') }}</label>
              <input
                id="filter-start"
                v-model="filters.start"
                type="datetime-local"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-end">{{ t('filters.end') }}</label>
              <input
                id="filter-end"
                v-model="filters.end"
                type="datetime-local"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-method">{{ t('filters.method') }}</label>
              <input
                id="filter-method"
                v-model="filters.method"
                placeholder="GET, POST"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-endpoint">{{ t('filters.endpoint') }}</label>
              <input
                id="filter-endpoint"
                v-model="filters.endpoint"
                placeholder="/api/orders/:id"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-status">{{ t('filters.statusGroup') }}</label>
              <select
                id="filter-status"
                v-model="filters.statusGroup"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              >
                <option value="all">{{ t('filters.all') }}</option>
                <option value="2">2xx</option>
                <option value="3">3xx</option>
                <option value="4">4xx</option>
                <option value="5">5xx</option>
              </select>
            </div>
            <div class="flex flex-col gap-2">
              <label class="text-sm font-medium text-foreground" for="filter-topn">{{ t('filters.topN') }}</label>
              <select
                id="filter-topn"
                v-model.number="filters.topN"
                class="rounded-md border border-muted-foreground/40 bg-background px-3 py-2 text-sm text-foreground"
              >
                <option :value="5">{{ t('filters.topX', { n: 5 }) }}</option>
                <option :value="10">{{ t('filters.topX', { n: 10 }) }}</option>
                <option :value="20">{{ t('filters.topX', { n: 20 }) }}</option>
                <option :value="30">{{ t('filters.topX', { n: 30 }) }}</option>
              </select>
              <label class="flex items-center gap-2 text-sm text-muted-foreground">
                <input v-model="filters.includeOther" type="checkbox" class="h-4 w-4" />
                {{ t('filters.includeOther') }}
              </label>
            </div>
            <div class="flex flex-col gap-2">
              <Button type="button" :disabled="isLoading" @click="applyFilters">
                {{ isLoading ? t('buttons.refreshing') : t('buttons.apply') }}
              </Button>
              <Button type="button" variant="outline" :disabled="isExporting" @click="exportChart('All')">
                {{ isExporting ? t('buttons.exporting') : t('buttons.exportAll') }}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </section>
</template>

<i18n lang="json">
{
  "en": {
    "dashboard_usage": {
      "title": "Interactive Guide",
      "summary": "This overview combines key performance indicators. Use the sections below for specialized analysis.",
      "traffic": "View request volume over time. The list shows your busiest endpoints; you can filter the whole dashboard by clicking any endpoint path.",
      "status": "Monitor for 4xx (client) and 5xx (server) errors. Select a status code to see exactly which requests are failing.",
      "latency": "Track performance percentiles. High P99 values indicate slow user experiences that may need optimization.",
      "heatmap": "Visualize traffic density across time and endpoints. Brighter areas indicate higher request volumes.",
      "details": "View raw log entries with full metadata. You can sort by any column or export the filtered results to Excel."
    },
    "latency_ms": "{value} ms",
    "nav": {
      "title": "Analytics Sections",
      "subtitle": "Switch between different visualization modes."
    },
    "summary": {
      "title": "Summary",
      "subtitle": "High-level metrics.",
      "empty": "No overall metrics available for these filters.",
      "cards": {
        "totalRequests": "Total Requests",
        "requests": "Total Requests",
        "avgLatency": "Avg Latency",
        "errorRate": "Error Rate (5xx)",
        "uniqueIps": "Unique IPs",
        "distinctEndpoints": "Distinct Endpoints",
        "p50": "P50 (Median)",
        "p95": "P95",
        "p99": "P99"
      }
    },
    "drilldown": {
      "traffic": "Traffic Distribution",
      "status": "HTTP Status Distribution",
      "latency": "Latency Percentiles",
      "heatmap": "Request Heatmap",
      "details": "Details",
      "label": "Drilldown",
      "detailsTitle": "Detailed logs"
    },
    "traffic": {
      "title": "Traffic Distribution",
      "subtitle": "Requests per endpoint.",
      "instruction": "Click on any endpoint to filter by that path.",
      "chartLabel": "Requests",
      "empty": "No traffic data for these filters."
    },
    "status_dist": {
      "title": "HTTP Status Distribution",
      "subtitle": "Breakdown of response codes.",
      "instruction": "Select a category to drill down into specific responses.",
      "chartLabel": "Count",
      "empty": "No status data for these filters."
    },
    "latency_percent": {
      "title": "Latency Percentiles",
      "subtitle": "Performance distribution in milliseconds.",
      "average": "Average Latency",
      "chartLabel": "ms",
      "p50": "P50 (Median)",
      "p75": "P75",
      "p90": "P90",
      "p95": "P95",
      "p99": "P99",
      "empty": "No latency data for these filters."
    },
    "heatmap": {
      "title": "Request Heatmap",
      "subtitle": "Traffic density over time and endpoint.",
      "instruction": "Darker colors represent higher traffic during that hour.",
      "yLabel": "Endpoint",
      "xLabel": "Date/Time",
      "empty": "No heatmap data for these filters."
    },
    "details": {
      "title": "Detail panel",
      "subtitle": "Summary metrics and raw entries.",
      "empty": "No entries for the selected filters."
    },
    "table": {
      "timestamp": "Timestamp",
      "method": "Method",
      "endpoint": "Endpoint",
      "status": "Status",
      "latency": "Latency",
      "clientIp": "Client IP",
      "user": "User",
      "userAgent": "User Agent",
      "pagination": "Page {page} of {totalPages} ({count} entries)"
    },
    "filters": {
      "title": "Filters",
      "subtitle": "Refine analytics results.",
      "start": "Start",
      "end": "End",
      "method": "Method",
      "endpoint": "Endpoint",
      "statusGroup": "Status group",
      "all": "All",
      "topN": "Top endpoints",
      "topX": "Top {n}",
      "includeOther": "Include Other bucket"
    },
    "buttons": {
      "exporting": "Exporting...",
      "export": "Export to Excel",
      "exportTraffic": "Export Traffic",
      "exportStatus": "Export Status",
      "exportLatency": "Export Latency",
      "exportHeatmap": "Export Heatmap",
      "exportEntries": "Export to Excel",
      "exportAll": "Export all charts",
      "apply": "Apply filters",
      "refreshing": "Refreshing...",
      "prev": "Prev",
      "next": "Next"
    }
  },
  "de": {
    "dashboard_usage": {
      "title": "Interaktive Hilfe",
      "summary": "Diese Übersicht kombiniert wichtige Leistungskennzahlen. Nutzen Sie die Bereiche unten für spezialisierte Analysen.",
      "traffic": "Anfragevolumen im Zeitverlauf. Die Liste zeigt die am stärksten frequentierten Endpunkte; Filtern Sie das Dashboard per Klick auf einen Pfad.",
      "status": "Überwachen Sie 4xx (Client) und 5xx (Server) Fehler. Wählen Sie einen Statuscode aus, um gezielt fehlerhafte Anfragen zu analysieren.",
      "latency": "Verfolgen Sie Latenz-Perzentile. Hohe P99-Werte deuten auf Verzögerungen hin, die optimiert werden sollten.",
      "heatmap": "Visualisieren Sie die Verkehrsdichte über Zeit und Endpunkte. Hellere Bereiche weisen auf höheres Aufkommen hin.",
      "details": "Rohe Protokolleinträge mit allen Metadaten. Sie können Spalten sortieren oder die gefilterten Ergebnisse als Excel exportieren."
    },
    "latency_ms": "{value} ms",
    "nav": {
      "title": "Analysebereiche",
      "subtitle": "Zwischen verschiedenen Visualisierungsmodi wechseln."
    },
    "summary": {
      "title": "Zusammenfassung",
      "subtitle": "Wichtige Metriken auf einen Blick.",
      "empty": "Keine Metriken für diese Filter verfügbar.",
      "cards": {
        "totalRequests": "Anfragen gesamt",
        "requests": "Anfragen gesamt",
        "avgLatency": "Durchschn. Latenz",
        "errorRate": "Fehlerrate (5xx)",
        "uniqueIps": "Eindeutige IPs",
        "distinctEndpoints": "Verschiedene Endpunkte",
        "p50": "P50 (Median)",
        "p95": "P95",
        "p99": "P99"
      }
    },
    "drilldown": {
      "traffic": "Verkehrsverteilung",
      "status": "HTTP-Statusverteilung",
      "latency": "Latenz-Perzentile",
      "heatmap": "Anfrage-Heatmap",
      "details": "Details",
      "label": "Drilldown",
      "detailsTitle": "Detaillierte Protokolle"
    },
    "traffic": {
      "title": "Verkehrsverteilung",
      "subtitle": "Anfragen pro Endpunkt.",
      "instruction": "Klicken Sie auf einen Endpunkt, um nach diesem Pfad zu filtern.",
      "chartLabel": "Anfragen",
      "empty": "Keine Verkehrsdaten für diese Filter."
    },
    "status_dist": {
      "title": "HTTP-Statusverteilung",
      "subtitle": "Aufschlüsselung der Antwortcodes.",
      "instruction": "Wählen Sie eine Kategorie, um spezifische Antworten zu analysieren.",
      "chartLabel": "Anzahl",
      "empty": "Keine Statusdaten für diese Filter."
    },
    "latency_percent": {
      "title": "Latenz-Perzentile",
      "subtitle": "Leistungsverteilung in Millisekunden.",
      "average": "Durchschnittliche Latenz",
      "chartLabel": "ms",
      "p50": "P50 (Median)",
      "p75": "P75",
      "p90": "P90",
      "p95": "P95",
      "p99": "P99",
      "empty": "Keine Latenzdaten für diese Filter."
    },
    "heatmap": {
      "title": "Anfrage-Heatmap",
      "subtitle": "Verkehrsdichte über Zeit und Endpunkt.",
      "instruction": "Dunklere Farben stehen für höheres Aufkommen in dieser Stunde.",
      "yLabel": "Endpunkt",
      "xLabel": "Datum/Zeit",
      "empty": "Keine Heatmap-Daten für diese Filter."
    },
    "details": {
      "title": "Detailansicht",
      "subtitle": "Zusammenfassende Metriken und Rohdaten.",
      "empty": "Keine Einträge für die gewählten Filter."
    },
    "table": {
      "timestamp": "Zeitstempel",
      "method": "Methode",
      "endpoint": "Endpunkt",
      "status": "Status",
      "latency": "Latenz",
      "clientIp": "Client-IP",
      "user": "Benutzer",
      "userAgent": "User-Agent",
      "pagination": "Seite {page} von {totalPages} ({count} Einträge)"
    },
    "filters": {
      "title": "Filter",
      "subtitle": "Analyseergebnisse verfeinern.",
      "start": "Beginn",
      "end": "Ende",
      "method": "Methode",
      "endpoint": "Endpunkt",
      "statusGroup": "Statusgruppe",
      "all": "Alle",
      "topN": "Top-Endpunkte",
      "topX": "Top {n}",
      "includeOther": "Kategorie 'Andere' einschließen"
    },
    "buttons": {
      "exporting": "Exportiere...",
      "export": "Daten exportieren",
      "exportTraffic": "Verkehr exportieren",
      "exportStatus": "Status exportieren",
      "exportLatency": "Latenz exportieren",
      "exportHeatmap": "Heatmap exportieren",
      "exportEntries": "Einträge exportieren",
      "exportAll": "Alle Diagramme exportieren",
      "apply": "Filter anwenden",
      "refreshing": "Aktualisiere...",
      "prev": "Zurück",
      "next": "Weiter"
    }
  }
}
</i18n>
