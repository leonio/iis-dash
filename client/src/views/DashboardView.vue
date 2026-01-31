<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import DashboardSection from '@/components/DashboardSection.vue'

const route = useRoute()
const section = computed(() => {
  const value = route.params.section
  return typeof value === 'string' ? value : undefined
})
</script>

<template>
  <DashboardSection :section="section" />
</template>
