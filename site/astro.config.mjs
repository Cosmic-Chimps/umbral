import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';
import { base, repository, site } from './site.config.mjs';
import { sidebar } from './sidebar.mjs';

// No custom components, no layouts, no head scripts: Starlight's defaults plus
// one stylesheet. The site asks no other host for anything, and
// scripts/check-dist.mjs proves it after every build.

// What a chat or social app shows beside a link to the site. Starlight writes
// the title and the description but no picture, and its favicon is an SVG,
// which those apps do not draw: without these the preview has an empty box
// where the picture goes and no icon. The picture's address must be absolute.
const linkPreview = [
  { tag: 'meta', attrs: { property: 'og:image', content: `${site}${base}/og.png` } },
  { tag: 'meta', attrs: { property: 'og:image:type', content: 'image/png' } },
  { tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
  { tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
  { tag: 'meta', attrs: { property: 'og:image:alt', content: 'Umbral: self-hosted passwordless sign-in' } },
  { tag: 'meta', attrs: { name: 'twitter:image', content: `${site}${base}/og.png` } },
  { tag: 'link', attrs: { rel: 'icon', type: 'image/png', sizes: '32x32', href: `${base}/favicon-32.png` } },
  { tag: 'link', attrs: { rel: 'apple-touch-icon', sizes: '180x180', href: `${base}/apple-touch-icon.png` } },
];
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
      head: linkPreview,
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
