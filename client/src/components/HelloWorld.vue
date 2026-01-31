<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

defineProps<{ msg: string }>()

const count = ref(0)
</script>

<template>
  <h1>{{ msg }}</h1>

  <div class="card">
    <button type="button" @click="count++">{{ t('count', { count }) }}</button>
    <p v-html="t('edit_prompt', { file: '<code>components/HelloWorld.vue</code>' })">
    </p>
  </div>

  <p>
    {{ t('check_out') }}
    <a href="https://vuejs.org/guide/quick-start.html#local" target="_blank"
      >create-vue</a
    >, {{ t('starter') }}
  </p>
  <p v-html="t('learn_more', { link: '<a href=\'https://vuejs.org/guide/scaling-up/tooling.html#ide-support\' target=\'_blank\'>Vue Docs Scaling up Guide</a>' })">
  </p>
  <p class="read-the-docs">{{ t('click_logos') }}</p>
</template>

<i18n lang="json">
{
  "en": {
    "count": "count is {count}",
    "edit_prompt": "Edit {file} to test HMR",
    "check_out": "Check out",
    "starter": "the official Vue + Vite starter",
    "learn_more": "Learn more about IDE Support for Vue in the {link}.",
    "click_logos": "Click on the Vite and Vue logos to learn more"
  },
  "de": {
    "count": "Zähler ist {count}",
    "edit_prompt": "Bearbeite {file} um HMR zu testen",
    "check_out": "Probieren Sie",
    "starter": "den offiziellen Vue + Vite Starter aus",
    "learn_more": "Erfahren Sie mehr über IDE-Unterstützung für Vue im {link}.",
    "click_logos": "Klicken Sie auf die Vite- und Vue-Logos, um mehr zu erfahren"
  }
}
</i18n>

<style scoped>
.read-the-docs {
  color: #888;
}
</style>
