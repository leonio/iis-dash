import { ref } from 'vue'

export function useApi() {
  const baseUrl = ref('http://localhost:5039')

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

  return {
    baseUrl,
    getHello,
    precheckLogFile,
    uploadLogFiles,
    getUploads,
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
