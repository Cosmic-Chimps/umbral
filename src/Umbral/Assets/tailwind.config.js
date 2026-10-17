// Umbral's design tokens: the single source of both the Tailwind theme (utilities
// such as bg-surface-container-low, text-on-surface-variant, rounded-lg) and the
// CSS custom properties (--color-primary, --radius-md, ...) that the component
// layer in umbral.input.css and a few inline places use directly.
//
// The Umbra look (specs/007-umbra-look): ink, bone and a corona of gold, in two
// themes. The dark set is written on `:root` (it is also the fallback); the light
// set on `:root[data-theme="light"]` and, under `data-theme="system"`, inside
// `@media (prefers-color-scheme: light)`. The server always writes data-theme on
// <html> (Views/Layout.fs), so the theme is decided before the first paint, with
// no script. Every value below is contrast-checked: see plan.md "Token Mapping".
//
// Compiled to umbral.css by build-css.sh. Rule: class names are never built by
// interpolation -- only literal strings in views, so the scanner below (it reads
// Views/ and Handlers/) always sees them.

// One table per theme, same names in both. `primary` is the accent as text and
// lines (links, focus ring, checked choice); `primary-fill` / `on-primary-fill`
// are the primary button's fill and its text, which differ from it in light.
const dark = {
  'surface': '#0b0b14',
  'surface-container-lowest': '#07070e',
  'surface-container-low': '#14141f',
  'surface-container': '#181826',
  'surface-container-high': '#1d1d2b',
  'surface-container-highest': '#26263a',
  'on-surface': '#ece8df',
  'on-surface-variant': '#a7a2b6',
  'outline': '#74708a',
  'outline-variant': '#2e2e40',
  'primary': '#f5c26b',
  'on-primary': '#2a1d05',
  'primary-fill': '#f5c26b',
  'on-primary-fill': '#2a1d05',
  'primary-container': '#3a2e12',
  'secondary': '#a7a2b6',
  'secondary-container': '#26263a',
  'success': '#a8d99a',
  'success-container': '#20301f',
  'warning': '#f0b45e',
  'warning-container': '#2a2111',
  'warning-outline': '#6b5328',
  'error': '#ff8f7e',
  'on-error': '#3a0b05',
  'error-container': '#3a1613',
  'on-error-container': '#ffdad4',
};

const light = {
  'surface': '#f4f1ea',
  'surface-container-lowest': '#ebe5d8',
  'surface-container-low': '#fffdf8',
  'surface-container': '#faf7f0',
  'surface-container-high': '#ebe5d8',
  'surface-container-highest': '#e3ddd0',
  'on-surface': '#17151f',
  'on-surface-variant': '#5f5a6b',
  'outline': '#857e6e',
  'outline-variant': '#ddd6c8',
  'primary': '#8a5a00',
  'on-primary': '#fffdf8',
  'primary-fill': '#17151f',
  'on-primary-fill': '#f5c26b',
  'primary-container': '#fbefd6',
  'secondary': '#5f5a6b',
  'secondary-container': '#e3ddd0',
  'success': '#2f5d1e',
  'success-container': '#e6eedc',
  'warning': '#6b4500',
  'warning-container': '#fbefd6',
  'warning-outline': '#e2c27f',
  'error': '#b3261e',
  'on-error': '#ffffff',
  'error-container': '#f9dedb',
  'on-error-container': '#410e0b',
};

// The Gnomon's colours (research R6). Plain values, not channels: SVG parts take
// them through CSS classes (`.um-mark-*`), and no accent rule ever sets them.
const darkMark = {
  '--mark-sun': '#f5c26b',
  '--mark-base': '#3a3a52',
  '--mark-shadow': '#a7a2b6',
  '--mark-shadow-opacity': '0.5',
  '--mark-upright': '#ece8df',
};

const lightMark = {
  '--mark-sun': '#b8801f',
  '--mark-base': '#ddd6c8',
  '--mark-shadow': '#5f5a6b',
  '--mark-shadow-opacity': '0.4',
  '--mark-upright': '#17151f',
};

// '#f5c26b' -> '245 194 107'
function channels(hex) {
  const n = parseInt(hex.slice(1), 16);
  return `${(n >> 16) & 255} ${(n >> 8) & 255} ${n & 255}`;
}

// Every colour as channels (--color-X-rgb, which `/branding/accent/{hex}.css`
// overrides under `[data-accent]` for the primary ones: specs/006 research R4)
// plus the resolved colour (--color-X). `<alpha-value>` in the Tailwind theme
// below keeps opacity utilities (bg-primary/10) working in both themes.
function colorProperties(table, mark) {
  const properties = {};

  for (const [name, hex] of Object.entries(table)) {
    properties[`--color-${name}-rgb`] = channels(hex);
    properties[`--color-${name}`] = `rgb(var(--color-${name}-rgb))`;
  }

  return { ...properties, ...mark };
}

// name: [size, { lineHeight, fontWeight, letterSpacing }]
const typeStyles = {
  'display': ['34px', { lineHeight: '40px', fontWeight: '800', letterSpacing: '-0.035em' }],
  'headline-lg': ['24px', { lineHeight: '32px', fontWeight: '800', letterSpacing: '-0.03em' }],
  'headline-md': ['20px', { lineHeight: '28px', fontWeight: '600', letterSpacing: '-0.01em' }],
  'body-lg': ['16px', { lineHeight: '24px', fontWeight: '400', letterSpacing: '0em' }],
  'body-md': ['14px', { lineHeight: '20px', fontWeight: '400', letterSpacing: '0em' }],
  'label-md': ['13px', { lineHeight: '18px', fontWeight: '600', letterSpacing: '0.01em' }],
  'label-sm': ['11px', { lineHeight: '14px', fontWeight: '700', letterSpacing: '0.06em' }],
};

const radii = {
  'sm': '0.25rem',
  'DEFAULT': '0.5rem',
  'md': '0.75rem',
  'lg': '1rem',
  'full': '9999px',
};

// Custom properties only: not part of the Tailwind theme.
const spaces = {
  'xs': '0.25rem',
  'sm': '0.5rem',
  'md': '1rem',
  'lg': '1.5rem',
  'xl': '2rem',
};

// The fonts are self-hosted (@font-face in umbral.input.css); the system stacks
// are what shows while they load, or if a browser cannot use them.
const fontFamilies = {
  sans: ['"Schibsted Grotesk"', 'ui-sans-serif', 'system-ui', '-apple-system', '"Segoe UI"', 'sans-serif'],
  display: ['"Bricolage Grotesque"', '"Schibsted Grotesk"', 'ui-sans-serif', 'system-ui', 'sans-serif'],
  mono: ['"Spline Sans Mono"', 'ui-monospace', '"SF Mono"', 'monospace'],
};

const effects = {
  '--border-micro': '1px solid var(--color-outline-variant)',
  '--border-ghost': '1px solid var(--color-outline)',
  '--shadow-level-1': '0 1px 2px 0 rgb(0 0 0 / 0.4)',
  '--shadow-level-2': '0 10px 28px -8px rgb(0 0 0 / 0.55)',
  '--font-family': fontFamilies.sans.join(', '),
  '--font-display': fontFamilies.display.join(', '),
  '--font-mono': fontFamilies.mono.join(', '),
};

// Every custom property that does not change with the theme.
const customProperties = {};

// The Tailwind theme reads every colour through its variable, so every utility
// (bg-primary, text-on-primary, border-primary/25) follows the theme and the
// accent override.
const themeColors = {};

for (const name of Object.keys(dark)) {
  themeColors[name] = `rgb(var(--color-${name}-rgb) / <alpha-value>)`;
}

for (const [name, [size, style]] of Object.entries(typeStyles)) {
  customProperties[`--text-${name}-size`] = size;
  customProperties[`--text-${name}-line`] = style.lineHeight;
  customProperties[`--text-${name}-weight`] = style.fontWeight;
  customProperties[`--text-${name}-tracking`] = style.letterSpacing;
}

for (const [name, value] of Object.entries(radii)) {
  customProperties[`--radius-${name === 'DEFAULT' ? 'default' : name}`] = value;
}

for (const [name, value] of Object.entries(spaces)) {
  customProperties[`--space-${name}`] = value;
}

Object.assign(customProperties, effects);

module.exports = {
  // relative: paths resolve against this file, not the directory the CLI runs in.
  content: { relative: true, files: ['../Views/**/*.fs', '../Handlers/**/*.fs'] },
  theme: {
    extend: {
      colors: themeColors,
      fontSize: typeStyles,
      borderRadius: radii,
      fontFamily: fontFamilies,
      maxWidth: { page: '1120px' },
      boxShadow: { 'level-1': 'var(--shadow-level-1)', 'level-2': 'var(--shadow-level-2)' },
    },
  },
  plugins: [
    function ({ addBase }) {
      const lightProperties = colorProperties(light, lightMark);

      addBase({
        ':root': { 'color-scheme': 'dark', ...customProperties, ...colorProperties(dark, darkMark) },
        ':root[data-theme="light"]': { 'color-scheme': 'light', ...lightProperties },
        '@media (prefers-color-scheme: light)': {
          ':root[data-theme="system"]': { 'color-scheme': 'light', ...lightProperties },
        },
      });
    },
  ],
};
