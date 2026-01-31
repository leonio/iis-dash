<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
import { Button } from '@/components/ui/button'
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
          <RouterLink to="/" class="no-underline">
            <Button variant="outline" class="w-full justify-start">Dashboard</Button>
          </RouterLink>
          <RouterLink to="/admin" class="no-underline">
            <Button variant="outline" class="w-full justify-start">Admin</Button>
          </RouterLink>
        </nav>
      </aside>
      <main class="flex-1">
        <header class="border-b border-muted-foreground/20 bg-card">
          <div class="mx-auto flex w-full max-w-6xl items-center justify-between px-8 py-5">
            <div>
              <h2 class="text-lg font-semibold text-foreground">Dashboard</h2>
              <p class="text-sm text-muted-foreground">Monitor and manage your IIS logs.</p>
            </div>
          </div>
        </header>
        <div class="mx-auto w-full max-w-6xl px-8 py-8">
          <RouterView />
        </div>
      </main>
    </div>
  </div>
</template>
