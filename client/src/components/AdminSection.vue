<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { useApi, type LogUploadDto, type PrecheckResult, type UploadSummary } from '@/composables/useApi'

const { precheckLogFile, uploadLogFiles, getUploads } = useApi()

const selectedFiles = ref<File[]>([])
const precheckResult = ref<PrecheckResult | null>(null)
const precheckError = ref<string | null>(null)
const uploadResult = ref<UploadSummary | null>(null)
const uploadError = ref<string | null>(null)
const isUploading = ref(false)
const isPrechecking = ref(false)
const maxFileSizeBytes = 10 * 1024 * 1024
const uploads = ref<LogUploadDto[]>([])
const uploadsError = ref<string | null>(null)
const isLoadingUploads = ref(false)
const isDragging = ref(false)

const canUpload = computed(
  () =>
    selectedFiles.value.length > 0 &&
    precheckResult.value?.supported === true &&
    !isUploading.value,
)

const resetMessages = () => {
  precheckResult.value = null
  precheckError.value = null
  uploadResult.value = null
  uploadError.value = null
}

const runPrecheck = async (files: File[]) => {
  if (files.length === 0) {
    return
  }

  isPrechecking.value = true
  try {
    const firstFile = files[0]
    if (!firstFile) {
      return
    }

    precheckResult.value = await precheckLogFile(firstFile)
  } catch (error) {
    precheckError.value = error instanceof Error ? error.message : 'Precheck failed.'
  } finally {
    isPrechecking.value = false
  }
}

const setFiles = async (files: File[], target?: HTMLInputElement) => {
  selectedFiles.value = files
  resetMessages()

  if (files.length === 0) {
    return
  }

  const oversizeFile = files.find((file) => file.size > maxFileSizeBytes)
  if (oversizeFile) {
    precheckError.value = `"${oversizeFile.name}" exceeds the 10MB per-file limit.`
    selectedFiles.value = []
    if (target) {
      target.value = ''
    }
    return
  }

  await runPrecheck(files)
}

const handleFileChange = async (event: Event) => {
  const target = event.target as HTMLInputElement
  const files = target.files ? Array.from(target.files) : []
  await setFiles(files, target)
}

const handleDrop = async (event: DragEvent) => {
  event.preventDefault()
  isDragging.value = false
  const files = event.dataTransfer ? Array.from(event.dataTransfer.files) : []
  await setFiles(files)
}

const handleDragOver = (event: DragEvent) => {
  event.preventDefault()
  isDragging.value = true
}

const handleDragLeave = () => {
  isDragging.value = false
}

const handleUpload = async () => {
  if (!canUpload.value) {
    return
  }

  isUploading.value = true
  uploadError.value = null
  uploadResult.value = null

  try {
    uploadResult.value = await uploadLogFiles(selectedFiles.value)
    await loadUploads()
  } catch (error) {
    uploadError.value = error instanceof Error ? error.message : 'Upload failed.'
  } finally {
    isUploading.value = false
  }
}

const loadUploads = async () => {
  isLoadingUploads.value = true
  uploadsError.value = null

  try {
    uploads.value = await getUploads()
  } catch (error) {
    uploadsError.value = error instanceof Error ? error.message : 'Failed to load uploads.'
  } finally {
    isLoadingUploads.value = false
  }
}

const formatBytes = (value: number) => {
  if (value < 1024) {
    return `${value} B`
  }

  const units = ['KB', 'MB', 'GB']
  let size = value
  let unitIndex = -1

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024
    unitIndex += 1
  }

  return `${size.toFixed(1)} ${units[unitIndex]}`
}

onMounted(() => {
  void loadUploads()
})
</script>

<template>
  <section class="rounded-lg border border-muted-foreground/20 bg-card p-4">
    <div class="mb-4 flex items-center justify-between">
      <div>
        <h2 class="text-lg font-semibold text-foreground">Admin</h2>
        <p class="text-sm text-muted-foreground">Upload and monitor ingested log files.</p>
      </div>
      <Button type="button" variant="outline" :disabled="isLoadingUploads" @click="loadUploads">
        {{ isLoadingUploads ? 'Refreshing…' : 'Refresh uploads' }}
      </Button>
    </div>
    <div class="grid gap-6 md:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>Upload logs</CardTitle>
          <CardDescription>
            Upload IIS logs (W3C or IIS Log File Format). Zip files are supported; the first entry is
            used for a format precheck.
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label class="text-sm font-medium text-foreground" for="log-upload">
              Upload IIS logs (10MB max per file)
            </label>
            <p class="text-sm text-muted-foreground">
              Drag and drop files below or click to choose files from your PC. We run a quick
              format precheck before enabling upload.
            </p>
            <div
              class="flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed px-4 py-6 text-center text-sm transition"
              :class="
                isDragging
                  ? 'border-primary/70 bg-primary/5 text-foreground'
                  : 'border-muted-foreground/40 text-muted-foreground'
              "
              @dragover="handleDragOver"
              @dragleave="handleDragLeave"
              @drop="handleDrop"
            >
              <div class="font-medium text-foreground">Drop files here</div>
              <div>or</div>
              <label class="cursor-pointer rounded-md border border-muted-foreground/40 px-3 py-1 text-foreground hover:border-primary/60">
                Select files
                <input
                  id="log-upload"
                  type="file"
                  multiple
                  class="sr-only"
                  @change="handleFileChange"
                />
              </label>
              <div class="text-xs text-muted-foreground">Supported: W3C, IIS CSV, ZIP.</div>
            </div>
          </div>

          <div v-if="isPrechecking" class="text-sm text-muted-foreground">Checking format…</div>
          <div v-else-if="precheckResult" class="text-sm text-foreground">
            <span class="font-medium">Precheck:</span>
            {{ precheckResult.message }} (Format: {{ precheckResult.format }})
          </div>
          <div v-else-if="precheckError" class="text-sm text-destructive">
            {{ precheckError }}
          </div>

          <div v-if="uploadError" class="text-sm text-destructive">
            {{ uploadError }}
          </div>

          <div v-if="uploadResult" class="rounded-md border border-muted-foreground/40 p-3 text-sm">
            <div class="font-medium text-foreground">Ingestion summary</div>
            <div class="text-muted-foreground">
              Files: {{ uploadResult.processedFiles }}/{{ uploadResult.totalFiles }} processed,
              {{ uploadResult.unsupportedFiles }} unsupported, {{ uploadResult.rejectedFiles }} rejected,
              {{ uploadResult.emptyFiles }} empty
            </div>
            <div class="text-muted-foreground">
              Lines: {{ uploadResult.totalLines }} total, {{ uploadResult.parsedLines }} parsed,
              {{ uploadResult.failedLines }} failed, {{ uploadResult.skippedLines }} skipped
            </div>
          </div>
        </CardContent>
        <CardFooter>
          <Button type="button" :disabled="!canUpload" @click="handleUpload">
            {{ isUploading ? 'Uploading…' : 'Upload logs' }}
          </Button>
        </CardFooter>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Uploaded files</CardTitle>
          <CardDescription>Metadata for ingested log files.</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <div v-if="uploadsError" class="text-sm text-destructive">{{ uploadsError }}</div>
          <div v-else-if="isLoadingUploads" class="text-sm text-muted-foreground">
            Loading uploads…
          </div>
          <div v-else-if="uploads.length === 0" class="text-sm text-muted-foreground">
            No uploads yet.
          </div>
          <div v-else class="space-y-3">
            <div
              v-for="upload in uploads"
              :key="upload.id"
              class="rounded-md border border-muted-foreground/30 p-3 text-sm"
            >
              <div class="font-medium text-foreground">{{ upload.originalFileName }}</div>
              <div class="text-muted-foreground">
                {{ upload.format }} · {{ formatBytes(upload.fileSizeBytes) }} ·
                {{ new Date(upload.uploadedAt).toLocaleString() }}
              </div>
              <div class="text-muted-foreground">
                Lines: {{ upload.totalLines }} total, {{ upload.parsedLines }} parsed,
                {{ upload.failedLines }} failed, {{ upload.skippedLines }} skipped
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </section>
</template>
