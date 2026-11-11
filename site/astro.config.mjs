import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';
import { base, repository, site } from './site.config.mjs';
import { sidebar } from './sidebar.mjs';

// No custom components, no layouts, no head scripts: Starlight's defaults plus
// one stylesheet and one `head` entry, the policy below. The site asks no other
// host for anything: scripts/check-dist.mjs proves it after every build, and the
// policy has the browser refuse it.

// GitHub Pages sends no header of ours, so the Content-Security-Policy travels in
// each page, as a meta element. Everything comes from the site itself; each
// exception is something a browser refused under a bare default-src 'self'.
const contentSecurityPolicy = [
  "default-src 'self'",
  // 'unsafe-inline': Starlight's inline scripts (the theme before first paint, the
  // sidebar's state). 'wasm-unsafe-eval': Pagefind's WebAssembly, when its worker
  // cannot start and the search runs on the page.
  "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'",
  // The style attributes Starlight and the code blocks write (icon sizes, token colours).
  "style-src 'self' 'unsafe-inline'",
  // The icons the stylesheets hold as data: URLs.
  "img-src 'self' data:",
  // The two that default-src does not cover: no <base>, and no form that posts elsewhere.
  "base-uri 'none'",
  "form-action 'self'",
].join('; ');

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
      // Starlight puts an http-equiv meta right after the charset and the viewport,
      // before the first script and stylesheet, which is where a policy has to be.
      head: [{ tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: contentSecurityPolicy } }],
    }),
  ],
});
