<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
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

const { t, d } = useI18n()
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
    precheckError.value = error instanceof Error ? error.message : t('errors.precheckFailed')
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
    precheckError.value = t('errors.fileTooLarge', { name: oversizeFile.name })
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
    uploadError.value = error instanceof Error ? error.message : t('errors.uploadFailed')
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
    uploadsError.value = error instanceof Error ? error.message : t('errors.loadUploadsFailed')
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
        <h2 class="text-lg font-semibold text-foreground">{{ t('title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('subtitle') }}</p>
      </div>
      <Button type="button" variant="outline" :disabled="isLoadingUploads" @click="loadUploads">
        {{ isLoadingUploads ? t('buttons.refreshing') : t('buttons.refresh') }}
      </Button>
    </div>
    <div class="grid gap-6 md:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>{{ t('uploader.title') }}</CardTitle>
          <CardDescription>
            {{ t('uploader.description') }}
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label class="text-sm font-medium text-foreground" for="log-upload">
              {{ t('uploader.label') }}
            </label>
            <p class="text-sm text-muted-foreground">
              {{ t('uploader.instruction') }}
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
              <div class="font-medium text-foreground">{{ t('uploader.drop') }}</div>
              <div>{{ t('uploader.or') }}</div>
              <label class="cursor-pointer rounded-md border border-muted-foreground/40 px-3 py-1 text-foreground hover:border-primary/60">
                {{ t('uploader.select') }}
                <input
                  id="log-upload"
                  type="file"
                  multiple
                  class="sr-only"
                  @change="handleFileChange"
                />
              </label>
              <div class="text-xs text-muted-foreground">{{ t('uploader.supported') }}</div>
            </div>
          </div>

          <div v-if="isPrechecking" class="text-sm text-muted-foreground">{{ t('status.checking') }}</div>
          <div v-else-if="precheckResult" class="text-sm text-foreground">
            <span class="font-medium">{{ t('status.precheckLabel') }}</span>
            {{ precheckResult.message }} ({{ t('status.formatLabel') }} {{ precheckResult.format }})
          </div>
          <div v-else-if="precheckError" class="text-sm text-destructive">
            {{ precheckError }}
          </div>

          <div v-if="uploadError" class="text-sm text-destructive">
            {{ uploadError }}
          </div>

          <div v-if="uploadResult" class="rounded-md border border-muted-foreground/40 p-3 text-sm">
            <div class="font-medium text-foreground">{{ t('summary.title') }}</div>
            <div class="text-muted-foreground">
              {{ t('summary.files', { 
                processed: uploadResult.processedFiles, 
                total: uploadResult.totalFiles,
                unsupported: uploadResult.unsupportedFiles,
                rejected: uploadResult.rejectedFiles,
                empty: uploadResult.emptyFiles
              }) }}
            </div>
            <div class="text-muted-foreground">
              {{ t('summary.lines', { 
                total: uploadResult.totalLines,
                parsed: uploadResult.parsedLines,
                failed: uploadResult.failedLines,
                skipped: uploadResult.skippedLines
              }) }}
            </div>
          </div>
        </CardContent>
        <CardFooter>
          <Button type="button" :disabled="!canUpload" @click="handleUpload">
            {{ isUploading ? t('buttons.uploading') : t('buttons.upload') }}
          </Button>
        </CardFooter>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>{{ t('history.title') }}</CardTitle>
          <CardDescription>{{ t('history.subtitle') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <div v-if="uploadsError" class="text-sm text-destructive">{{ uploadsError }}</div>
          <div v-else-if="isLoadingUploads" class="text-sm text-muted-foreground">
            {{ t('history.loading') }}
          </div>
          <div v-else-if="uploads.length === 0" class="text-sm text-muted-foreground">
            {{ t('history.empty') }}
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
                {{ d(new Date(upload.uploadedAt), 'long') }}
              </div>
              <div class="text-muted-foreground">
                {{ t('summary.lines', { 
                  total: upload.totalLines,
                  parsed: upload.parsedLines,
                  failed: upload.failedLines,
                  skipped: upload.skippedLines
                }) }}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </section>
</template>

<i18n lang="json">
{
  "en": {
    "title": "Admin",
    "subtitle": "Upload and monitor ingested log files.",
    "buttons": {
      "refresh": "Refresh uploads",
      "refreshing": "Refreshing…",
      "upload": "Upload logs",
      "uploading": "Uploading…"
    },
    "uploader": {
      "title": "Upload logs",
      "description": "Upload IIS logs (W3C or IIS Log File Format). Zip files are supported; the first entry is used for a format precheck.",
      "label": "Upload IIS logs (10MB max per file)",
      "instruction": "Drag and drop files below or click to choose files from your PC. We run a quick format precheck before enabling upload.",
      "drop": "Drop files here",
      "or": "or",
      "select": "Select files",
      "supported": "Supported: W3C, IIS CSV, ZIP."
    },
    "status": {
      "checking": "Checking format…",
      "precheckLabel": "Precheck:",
      "formatLabel": "Format:"
    },
    "summary": {
      "title": "Ingestion summary",
      "files": "Files: {processed}/{total} processed, {unsupported} unsupported, {rejected} rejected, {empty} empty",
      "lines": "Lines: {total} total, {parsed} parsed, {failed} failed, {skipped} skipped"
    },
    "history": {
      "title": "Uploaded files",
      "subtitle": "Metadata for ingested log files.",
      "loading": "Loading uploads…",
      "empty": "No uploads yet."
    },
    "errors": {
      "precheckFailed": "Precheck failed.",
      "uploadFailed": "Upload failed.",
      "loadUploadsFailed": "Failed to load uploads.",
      "fileTooLarge": "\"{name}\" exceeds the 10MB per-file limit."
    }
  },
  "de": {
    "title": "Verwaltung",
    "subtitle": "Hochgeladene Protokolldateien hochladen und überwachen.",
    "buttons": {
      "refresh": "Uploads aktualisieren",
      "refreshing": "Aktualisiere…",
      "upload": "Logs hochladen",
      "uploading": "Lade hoch…"
    },
    "uploader": {
      "title": "Logs hochladen",
      "description": "IIS-Logs (W3C- oder IIS-Logdateiformat) hochladen. ZIP-Dateien werden unterstützt; der erste Eintrag wird für eine Format-Vorprüfung verwendet.",
      "label": "IIS-Logs hochladen (max. 10 MB pro Datei)",
      "instruction": "Ziehen Sie Dateien hierher oder klicken Sie, um Dateien von Ihrem PC auszuwählen. Wir führen eine kurze Format-Vorprüfung durch, bevor wir den Upload aktivieren.",
      "drop": "Dateien hier ablegen",
      "or": "oder",
      "select": "Dateien auswählen",
      "supported": "Unterstützt: W3C, IIS CSV, ZIP."
    },
    "status": {
      "checking": "Format wird geprüft…",
      "precheckLabel": "Vorprüfung:",
      "formatLabel": "Format:"
    },
    "summary": {
      "title": "Zusammenfassung der Aufnahme",
      "files": "Dateien: {processed}/{total} verarbeitet, {unsupported} nicht unterstützt, {rejected} abgelehnt, {empty} leer",
      "lines": "Zeilen: {total} gesamt, {parsed} analysiert, {failed} fehlgeschlagen, {skipped} übersprungen"
    },
    "history": {
      "title": "Hochgeladene Dateien",
      "subtitle": "Metadaten für aufgenommene Protokolldateien.",
      "loading": "Lade Uploads…",
      "empty": "Noch keine Uploads vorhanden."
    },
    "errors": {
      "precheckFailed": "Vorprüfung fehlgeschlagen.",
      "uploadFailed": "Upload fehlgeschlagen.",
      "loadUploadsFailed": "Uploads konnten nicht geladen werden.",
      "fileTooLarge": "\"{name}\" überschreitet das Limit von 10 MB pro Datei."
    }
  }
}
</i18n>
