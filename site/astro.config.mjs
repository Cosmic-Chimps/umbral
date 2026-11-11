import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';
import { base, repository, site } from './site.config.mjs';
import { sidebar } from './sidebar.mjs';

// No custom components, no layouts, no head scripts: Starlight's defaults plus
// one stylesheet. The site asks no other host for anything, and
// scripts/check-dist.mjs proves it after every build.
export default defineConfig({
  site,
  base,
  trailingSlash: 'always',
  integrations: [
    starlight({
      title: 'Umbral',
      description:
        'Documentation for Umbral, a self-hosted OpenID Connect provider that signs people in without passwords.',
      logo: {
        light: './src/assets/gnomon-light.svg',
        dark: './src/assets/gnomon-dark.svg',
        alt: '',
      },
      favicon: '/favicon.svg',
      customCss: ['./src/styles/umbra.css'],
      social: [{ icon: 'github', label: 'GitHub', href: repository }],
      sidebar,
      plugins: [starlightLinksValidator()],
      lastUpdated: false,
      credits: false,
      pagefind: true,
    }),
  ],
});
