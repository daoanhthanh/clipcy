import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';

export default defineConfig({
  site: 'https://clipcy.dev',
  integrations: [sitemap()],
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'zh-CN', 'ja', 'ru', 'ko', 'vi'],
    routing: {
      prefixDefaultLocale: true
    }
  },
  build: {
    format: 'directory'
  }
});
