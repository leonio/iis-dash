<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import Cookies from 'js-cookie'
import { Button } from '@/components/ui/button'
import { Languages } from 'lucide-vue-next'

const { locale } = useI18n()

const toggleLocale = () => {
  const newLocale = locale.value === 'en' ? 'de' : 'en'
  locale.value = newLocale
  Cookies.set('locale', newLocale, { expires: 365, sameSite: 'lax' })
}
</script>

<template>
  <Button
    variant="ghost"
    size="sm"
    class="flex w-full items-center justify-start gap-2 px-2 text-muted-foreground hover:bg-muted"
    @click="toggleLocale"
  >
    <Languages class="h-4 w-4" />
    <span class="text-xs uppercase">{{ locale }}</span>
  </Button>
</template>
