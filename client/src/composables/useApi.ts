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

  return {
    baseUrl,
    getHello,
  }
}
