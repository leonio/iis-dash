<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { Button } from '@/components/ui/button'

const route = useRoute()

const headerInfo = computed(() => {
  if (route.name === 'admin') {
    return {
      title: 'Admin',
      subtitle: 'Upload and manage IIS log files.',
    }
  }

  const section = typeof route.params.section === 'string' ? route.params.section : 'summary'
  const sectionTitle =
    section === 'traffic'
      ? 'Traffic'
      : section === 'status'
        ? 'Status'
        : section === 'latency'
          ? 'Latency'
          : section === 'heatmap'
            ? 'Heatmap'
            : section === 'details'
              ? 'Details'
              : 'Summary'

  return {
    title: `Dashboard · ${sectionTitle}`,
    subtitle: 'Monitor and analyze your IIS log data.',
  }
})
</script>

<template>
  <div class="min-h-screen bg-background text-foreground">
    <div class="flex min-h-screen">
      <aside class="w-64 border-r border-muted-foreground/20 bg-card px-5 py-6">
        <div class="mb-8">
          <h1 class="text-xl font-semibold text-foreground">IIS Dash</h1>
          <p class="text-sm text-muted-foreground">Windows-authenticated insights.</p>
        </div>
        <nav class="flex flex-col gap-2">
          <RouterLink to="/dashboard" class="no-underline">
            <Button variant="outline" class="w-full justify-start">Dashboard</Button>
          </RouterLink>
          <RouterLink to="/admin" class="no-underline">
            <Button variant="outline" class="w-full justify-start">Admin</Button>
          </RouterLink>
        </nav>
      </aside>
      <main class="flex-1">
        <header class="border-b border-muted-foreground/20 bg-card">
          <div class="mx-auto flex w-full max-w-[1600px] items-center justify-between px-6 py-5 lg:px-10">
            <div>
              <h2 class="text-lg font-semibold text-foreground">{{ headerInfo.title }}</h2>
              <p class="text-sm text-muted-foreground">{{ headerInfo.subtitle }}</p>
            </div>
          </div>
        </header>
        <div class="mx-auto w-full max-w-[1600px] px-6 py-8 lg:px-10">
          <RouterView />
        </div>
      </main>
    </div>
  </div>
</template>
