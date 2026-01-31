import { ref } from 'vue'

export function useApi() {
  const isDev = import.meta.env.DEV
  const baseUrl = ref(isDev ? 'http://localhost:5039' : '')

  async function getHello() {
    const response = await fetch(`${baseUrl.value}/api/hello`, {
      credentials: 'include',
    })

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}`)
    }

    return response.text()
  }

  async function precheckLogFile(file: File) {
    const formData = new FormData()
    formData.append('files', file)

    const response = await fetch(`${baseUrl.value}/api/logs/precheck`, {
      method: 'POST',
      credentials: 'include',
      body: formData,
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    return response.json() as Promise<PrecheckResult>
  }

  async function uploadLogFiles(files: File[]) {
    const formData = new FormData()
    files.forEach((file) => formData.append('files', file))

    const response = await fetch(`${baseUrl.value}/api/logs/upload`, {
      method: 'POST',
      credentials: 'include',
      body: formData,
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    return response.json() as Promise<UploadSummary>
  }

  async function getUploads() {
    const response = await fetch(`${baseUrl.value}/api/logs/uploads`, {
      credentials: 'include',
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    return response.json() as Promise<LogUploadDto[]>
  }

  async function getLogMetrics(request: MetricsRequest) {
    const response = await fetch(`${baseUrl.value}/api/logs/metrics`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    return response.json() as Promise<LogMetricsResponse>
  }

  async function getHeatmap(request: HeatmapRequest) {
    const response = await fetch(`${baseUrl.value}/api/logs/heatmap`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    return response.json() as Promise<HeatmapResponse>
  }

  async function getLogEntries(request: LogEntriesRequest) {
    const response = await fetch(`${baseUrl.value}/api/logs/entries`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    return response.json() as Promise<LogEntriesResponse>
  }

  async function exportLogAnalytics(request: ExportRequest) {
    const mode = request.mode.toLowerCase()
    const path = request.mode === 'All' ? 'all' : mode
    const response = await fetch(`${baseUrl.value}/api/logs/export/${path}`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      const errorText = await response.text()
      throw new Error(errorText || `Request failed with status ${response.status}`)
    }

    const blob = await response.blob()
    const disposition = response.headers.get('content-disposition') ?? ''
    const fileNameMatch = /filename="?([^";]+)"?/i.exec(disposition)
    const fileName = fileNameMatch?.[1] ?? 'iis-logs-export.xlsx'

    return { blob, fileName }
  }

  async function getVersion() {
    try {
      const response = await fetch(`${baseUrl.value}/api/version`, {
        credentials: 'include',
      })
      if (response.ok) {
        return (await response.json()) as { version: string }
      }
    } catch (e) {
      console.error('Failed to fetch server version', e)
    }
    return { version: 'Unknown' }
  }

  return {
    baseUrl,
    getHello,
    precheckLogFile,
    uploadLogFiles,
    getUploads,
    getLogMetrics,
    getHeatmap,
    getLogEntries,
    exportLogAnalytics,
    getVersion,
  }
}

export interface PrecheckResult {
  supported: boolean
  format: 'Unknown' | 'W3C' | 'Iis'
  message: string
}

export interface UploadSummary {
  totalFiles: number
  processedFiles: number
  unsupportedFiles: number
  rejectedFiles: number
  emptyFiles: number
  totalLines: number
  parsedLines: number
  failedLines: number
  skippedLines: number
  fileResults: UploadFileResult[]
}

export interface UploadFileResult {
  fileName: string
  fileHash?: string
  format: 'Unknown' | 'W3C' | 'Iis'
  totalLines: number
  parsedLines: number
  failedLines: number
  skippedLines: number
  status: 'Processed' | 'Unsupported' | 'Rejected' | 'Empty'
  message: string
}

export interface LogUploadDto {
  id: number
  originalFileName: string
  fileHash: string
  format: string
  fileSizeBytes: number
  uploadedAt: string
  totalLines: number
  parsedLines: number
  failedLines: number
  skippedLines: number
}

export interface LogQueryFilter {
  start?: string | null
  end?: string | null
  method?: string | null
  endpoint?: string | null
  clientIp?: string | null
  username?: string | null
  statusGroup?: number | null
  logFileId?: number | null
  timeOfDayStartMinutes?: number | null
  timeOfDayEndMinutes?: number | null
}

export interface MetricsRequest {
  filter: LogQueryFilter
  trafficBucketMinutes: number
}

export interface LatencyPercentiles {
  averageMs: number | null
  p50Ms: number | null
  p95Ms: number | null
  p99Ms: number | null
  sampleCount: number
}

export interface TrafficPoint {
  bucketStart: string
  count: number
  requestsPerMinute: number
}

export interface StatusGroupPoint {
  group: string
  count: number
}

export interface LogMetricsResponse {
  totalRequests: number
  errorRequests: number
  errorRate: number
  latency: LatencyPercentiles
  traffic: TrafficPoint[]
  statusGroups: StatusGroupPoint[]
}

export interface HeatmapRequest {
  filter: LogQueryFilter
  topN: number
  includeOther: boolean
}

export interface HeatmapCell {
  endpointIndex: number
  bucketIndex: number
  count: number
}

export interface HeatmapResponse {
  bucketMinutes: number
  endpoints: string[]
  buckets: string[]
  cells: HeatmapCell[]
}

export interface LogEntriesRequest {
  filter: LogQueryFilter
  page: number
  pageSize: number
}

export interface LogEntryDto {
  id: number
  timestamp: string | null
  method: string | null
  endpoint: string
  normalizedEndpoint: string
  protocolStatus: number | null
  statusGroup: string
  timeTakenMs: number | null
  bytesSent: number | null
  bytesReceived: number | null
  clientIp: string | null
  username: string | null
  userAgent: string | null
  logFileId: number
}

export interface LogEntriesResponse {
  page: number
  pageSize: number
  totalCount: number
  items: LogEntryDto[]
}

export type ExportMode = 'All' | 'Traffic' | 'Status' | 'Latency' | 'Heatmap' | 'Entries'

export interface ExportRequest {
  filter: LogQueryFilter
  topN: number
  includeOther: boolean
  mode: ExportMode
}
