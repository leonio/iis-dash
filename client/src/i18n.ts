import { createI18n } from 'vue-i18n'
import Cookies from 'js-cookie'

// Import global translations (handled by @intlify/unplugin-vue-i18n)
import en from './locales/en.json'
import de from './locales/de.json'

const savedLocale = Cookies.get('locale') || 'en'

const i18n = createI18n({
  legacy: false,
  locale: savedLocale,
  fallbackLocale: 'en',
  messages: {
    en,
    de
  },
  numberFormats: {
    en: {
      decimal: {
        style: 'decimal',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
      }
    },
    de: {
      decimal: {
        style: 'decimal',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
      }
    }
  }
})

export default i18n
