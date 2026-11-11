// The sidebar of the site, and with it the list of pages. scripts/collect.mjs
// imports this file and fails when a generated page is not named here or when
// an entry names a page that was not generated, so a new document cannot be
// published without a place in the navigation, and a renamed one cannot leave
// a dead entry behind. An entry with a `slug` is a page (`index` is the home
// page); an entry with a `link` is not, and is skipped by that check.
import { branch, repository } from './site.config.mjs';

export const sidebar = [
  {
    label: 'Start',
    items: [
      { label: 'Introduction', slug: 'index' },
      { label: 'Quick start', slug: 'quick-start' },
      { label: 'Integrate an app', slug: 'integrate' },
      { label: 'Samples', slug: 'samples' },
      { label: 'Native and mobile apps', slug: 'native-apps' },
    ],
  },
  {
    label: 'Connectors',
    items: [
      { label: 'Discord', slug: 'connectors/discord' },
      { label: 'GitHub', slug: 'connectors/github' },
      { label: 'Google', slug: 'connectors/google' },
      { label: 'Your own OpenID Connect provider', slug: 'connectors/oidc' },
      { label: 'Passkeys', slug: 'connectors/passkey' },
      { label: 'Fediverse', slug: 'connectors/fediverse' },
    ],
  },
  {
    label: 'Who can sign in',
    items: [
      { label: 'Who can sign in', slug: 'who-can-sign-in' },
      { label: 'Branding', slug: 'branding' },
      { label: 'The look', slug: 'look' },
    ],
  },
  {
    label: 'Run it',
    items: [
      { label: 'HTTPS and proxies', slug: 'run/https' },
      { label: 'Back up your data', slug: 'run/backup' },
      { label: 'Passkeys for the admin area', slug: 'passkeys' },
      { label: 'Forgotten operator credential', slug: 'run/operator-credential' },
      {
        label: 'Secret stores',
        collapsed: false,
        items: [
          { label: 'Overview and moving secrets', slug: 'run/secret-stores' },
          { label: 'AWS Secrets Manager', slug: 'secret-stores/aws' },
          { label: 'Google Cloud Secret Manager', slug: 'secret-stores/gcp' },
          { label: 'Azure Key Vault', slug: 'secret-stores/azure' },
          { label: 'BellaBaxter', slug: 'secret-stores/bellabaxter' },
        ],
      },
      { label: 'Deploying on Caravana', slug: 'run/caravana' },
      { label: 'Settings reference', slug: 'run/settings' },
      { label: 'Troubleshooting', slug: 'run/troubleshooting' },
    ],
  },
  {
    label: 'Reference',
    items: [
      { label: 'Release notes', slug: 'release-notes' },
      { label: 'The vision', slug: 'vision' },
      {
        label: 'The constitution (on GitHub)',
        link: `${repository}/blob/${branch}/.specify/memory/constitution.md`,
      },
    ],
  },
];

/** The slugs of the pages the sidebar names, walking the nested groups. */
export function sidebarSlugs(items = sidebar) {
  return items.flatMap((item) => (item.items ? sidebarSlugs(item.items) : item.slug ? [item.slug] : []));
}
