<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import LanguageSwitcher from '@/components/LanguageSwitcher.vue'

const { t } = useI18n()
const route = useRoute()

const headerInfo = computed(() => {
  if (route.name === 'admin') {
    return {
      title: t('app.headers.admin'),
      subtitle: t('app.headers.adminSubtitle'),
    }
  }

  const sectionKey = (typeof route.params.section === 'string' ? route.params.section : 'summary') as
    | 'traffic'
    | 'status'
    | 'latency'
    | 'heatmap'
    | 'details'
    | 'summary'

  const sectionTitle = t(`sections.${sectionKey}`)

  return {
    title: t('app.headers.dashboard', { section: sectionTitle }),
    subtitle: t('app.headers.dashboardSubtitle'),
  }
})
</script>

<template>
  <div class="flex h-screen overflow-hidden bg-background text-foreground">
    <aside class="flex w-64 flex-col border-r border-muted-foreground/20 bg-card px-5 py-6">
      <div class="mb-8">
        <h1 class="text-xl font-semibold text-foreground">{{ t('app.title') }}</h1>
        <p class="text-sm text-muted-foreground">{{ t('app.subtitle') }}</p>
      </div>
      <nav class="flex flex-1 flex-col gap-2 overflow-y-auto">
        <RouterLink to="/dashboard" class="no-underline">
          <Button variant="outline" class="w-full justify-start">{{ t('app.nav.dashboard') }}</Button>
        </RouterLink>
        <RouterLink to="/admin" class="no-underline">
          <Button variant="outline" class="w-full justify-start">{{ t('app.nav.admin') }}</Button>
        </RouterLink>
      </nav>
      <div class="mt-auto pt-6">
        <LanguageSwitcher />
      </div>
    </aside>
    <main class="flex flex-1 flex-col min-w-0">
      <header class="border-b border-muted-foreground/20 bg-card">
        <div class="mx-auto flex w-full max-w-[1600px] items-center justify-between px-6 py-5 lg:px-10">
          <div>
            <h2 class="text-lg font-semibold text-foreground">{{ headerInfo.title }}</h2>
            <p class="text-sm text-muted-foreground">{{ headerInfo.subtitle }}</p>
          </div>
        </div>
      </header>
      <div class="flex-1 overflow-y-auto">
        <div class="mx-auto w-full max-w-[1600px] px-6 py-8 lg:px-10">
          <RouterView />
        </div>
      </div>
    </main>
  </div>
</template>
