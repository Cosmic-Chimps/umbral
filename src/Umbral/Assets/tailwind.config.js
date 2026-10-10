// Umbral's design tokens: the single source of both the Tailwind theme (utilities
// such as bg-surface-container-low, text-on-surface-variant, rounded-lg) and the
// CSS custom properties (--color-primary, --radius-md, ...) that the component
// layer in umbral.input.css and a few inline places use directly.
//
// Umbral's own palette: calm, dark, legible. A security product's chrome should
// disappear -- the one colour that should draw the eye is the one warning the
// operator about something destructive.
//
// Compiled to umbral.css by build-css.sh. Rule: class names are never built by
// interpolation -- only literal strings in views, so the scanner below (it reads
// Views/ and Handlers/) always sees them.

const colors = {
  'surface': '#0d1117',
  'surface-container-lowest': '#090c10',
  'surface-container-low': '#11161d',
  'surface-container': '#161c25',
  'surface-container-high': '#1f2631',
  'surface-container-highest': '#2a3340',
  'on-surface': '#e6e9ef',
  'on-surface-variant': '#9aa7b8',
  'outline': '#5b6b7f',
  'outline-variant': '#2d3845',
  'primary': '#5fb3e8',
  'on-primary': '#07263b',
  'primary-container': '#1f6fa8',
  'secondary': '#7fd8c4',
  'secondary-container': '#1d4a40',
  'success': '#7fd8a0',
  'warning': '#f0c85a',
  'error': '#f28b82',
  'on-error': '#430806',
  'error-container': '#7a211b',
  'on-error-container': '#ffdad4',
};

// name: [size, { lineHeight, fontWeight, letterSpacing }]
const typeStyles = {
  'display': ['34px', { lineHeight: '40px', fontWeight: '700', letterSpacing: '-0.02em' }],
  'headline-lg': ['24px', { lineHeight: '32px', fontWeight: '700', letterSpacing: '-0.015em' }],
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

const effects = {
  '--border-micro': '1px solid rgb(255 255 255 / 0.06)',
  '--border-ghost': '1px solid rgb(255 255 255 / 0.12)',
  '--shadow-level-1': '0 1px 2px 0 rgb(0 0 0 / 0.4)',
  '--shadow-level-2': '0 10px 28px -8px rgb(0 0 0 / 0.55)',
  '--font-family': 'ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif',
  '--font-mono': 'ui-monospace, "SF Mono", "JetBrains Mono", monospace',
};

// Every custom property this theme defines.
const customProperties = {};

for (const [name, value] of Object.entries(colors)) {
  customProperties[`--color-${name}`] = value;
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
      colors,
      fontSize: typeStyles,
      borderRadius: radii,
      fontFamily: {
        sans: ['ui-sans-serif', 'system-ui', 'sans-serif'],
        mono: ['ui-monospace', '"SF Mono"', 'monospace'],
      },
      maxWidth: { page: '1120px' },
      boxShadow: { 'level-1': 'var(--shadow-level-1)', 'level-2': 'var(--shadow-level-2)' },
    },
  },
  plugins: [
    function ({ addBase }) {
      addBase({ ':root': { 'color-scheme': 'dark', ...customProperties } });
    },
  ],
};
