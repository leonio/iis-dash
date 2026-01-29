<script setup lang="ts">
import { ref } from 'vue'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { VisXYContainer, VisArea } from '@unovis/vue'
import { useApi } from '@/composables/useApi'

interface ChartPoint {
  x: number
  y: number
}

const { getHello } = useApi()
const helloResponse = ref('')
const chartData = ref<ChartPoint[]>([
  { x: 0, y: 12 },
  { x: 1, y: 18 },
  { x: 2, y: 10 },
  { x: 3, y: 22 },
])

const handleHello = async () => {
  helloResponse.value = await getHello()
}
</script>

<template>
  <main class="min-h-screen bg-background p-8 text-foreground">
    <div class="mx-auto flex w-full max-w-3xl flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle>Hello World</CardTitle>
          <CardDescription>shadcn-vue + Tailwind v4 + Unovis</CardDescription>
        </CardHeader>
        <CardContent>
          <div class="h-40 rounded-md border border-dashed border-muted-foreground/40">
            <VisXYContainer :data="chartData" :height="160">
              <VisArea :x="(d: ChartPoint) => d.x" :y="(d: ChartPoint) => d.y" />
            </VisXYContainer>
          </div>
        </CardContent>
        <CardFooter class="flex flex-col items-start gap-2">
          <div class="flex items-center gap-2">
            <Button type="button" @click="handleHello">Call /api/hello</Button>
            <span class="text-sm text-muted-foreground">NTLM response below.</span>
          </div>
          <span v-if="helloResponse" class="text-sm text-foreground">{{ helloResponse }}</span>
        </CardFooter>
      </Card>
    </div>
  </main>
</template>
