import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

const app = document.getElementById('app')!;

// Small helper: build elements with textContent, never innerHTML, so a claim
// value (a display name someone chose) can only ever be text.
function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  text?: string,
  attrs: Record<string, string> = {},
): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (text !== undefined) e.textContent = text;
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
  return e;
}

function render(...nodes: Node[]) {
  app.replaceChildren(...nodes);
}

function link(href: string, text: string) {
  return el('a', text, { href });
}

// Runtime configuration. The bundle holds neither the issuer nor the client id:
// the dev server and nginx both answer /config.json from the environment, so
// one build runs against any Umbral. Nothing here needs a credential: a
// browser app cannot keep one, so Umbral gives it none.
async function loadConfig(): Promise<{ issuer: string; clientId: string }> {
  const response = await fetch('/config.json');
  return response.json();
}

function notConfiguredPage(missing: string[]) {
  render(
    el('h1', 'Not configured'),
    el('p', `Set ${missing.join(' and ')} in the environment of the server that serves this app, then reload.`),
  );
}

function createUserManager(config: { issuer: string; clientId: string }) {
  return new UserManager({
    // Discovery only: the endpoints are read from the issuer, never set by hand.
    authority: config.issuer,
    client_id: config.clientId,
    // Both come from configuration here, so a call never has to pass them. They
    // are the sample's own paths (the library has no defaults), and Umbral
    // matches them exactly.
    redirect_uri: `${window.location.origin}/signin-callback`,
    post_logout_redirect_uri: `${window.location.origin}/signout-callback`,
    // Authorization code with PKCE (S256), the only flow Umbral offers. The
    // library creates the verifier, and checks state and nonce on the way back.
    // Never turn any of that off.
    response_type: 'code',
    scope: 'openid profile email',
    // sessionStorage, not localStorage: the tokens are gone when the tab closes,
    // and no other tab or later visit can read them. The same for the sign-in
    // in flight (state, nonce, PKCE verifier), whose store the library would
    // otherwise default to localStorage.
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    // Umbral issues no refresh token to this app (we do not ask for
    // offline_access), so there is nothing to renew with. An expired token
    // simply sends the person through sign-in again.
    automaticSilentRenew: false,
    // The id_token already carries the claims, so no second call to userinfo.
    loadUserInfo: false,
  });
}

let userManager: UserManager;

async function currentUser(): Promise<User | null> {
  const user = await userManager.getUser();
  return user && !user.expired ? user : null;
}

function signIn(returnTo: string) {
  return userManager.signinRedirect({ state: { returnTo } });
}

async function homePage() {
  const user = await currentUser();
  const session = el('p');
  if (user) {
    const form = el('form', undefined, { method: 'post', action: '/signout' });
    form.append(el('button', 'Sign out', { type: 'submit' }));
    // Ends the local session and goes to Umbral's end-session endpoint with
    // the id_token as hint, so Umbral knows which app's post-logout redirect
    // URI to honour. The endpoint comes from discovery.
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      void userManager.signoutRedirect();
    });
    session.append(form);
  } else {
    const a = link('/signin', 'Sign in');
    a.addEventListener('click', (e) => {
      e.preventDefault();
      void signIn('/');
    });
    session.append(a);
  }
  const nav = el('p');
  nav.append(link('/public', 'Public'), ' ', link('/private', 'Private'));
  render(el('h1', 'Umbral sample: browser-only app'), nav, session);
}

function publicPage() {
  const back = el('p');
  back.append(link('/', 'Back'));
  render(el('h1', 'Public'), el('p', 'Anyone can read this page, signed in or not.'), back);
}

async function privatePage() {
  const user = await currentUser();
  if (!user) {
    // The browser leaves for Umbral and comes back to /signin-callback, which
    // returns here.
    await signIn('/private');
    return;
  }
  const table = el('table');
  // The claims as they come, no interpretation. email and email_verified are
  // there together or not at all.
  for (const name of ['sub', 'name', 'idp', 'email', 'email_verified']) {
    const value = (user.profile as Record<string, unknown>)[name];
    if (value === undefined) continue;
    const row = el('tr');
    row.append(el('th', name), el('td', String(value)));
    table.append(row);
  }
  const back = el('p');
  back.append(link('/', 'Back'));
  render(el('h1', 'Private'), table, back);
}

function notFoundPage() {
  const home = el('p');
  home.append(link('/', 'Home'));
  render(el('h1', 'Not found'), home);
}

function failurePage(error: unknown) {
  const home = el('p');
  home.append(link('/', 'Home'));
  render(el('h1', 'Sign-in failed'), el('p', error instanceof Error ? error.message : String(error)), home);
}

async function route(path: string) {
  switch (path) {
    case '/':
      return homePage();
    case '/public':
      return publicPage();
    case '/private':
      return privatePage();
    case '/signin':
      return signIn('/');
    case '/signin-callback': {
      // Exchanges the code for tokens (with the PKCE verifier) and checks
      // state and nonce. The URL then goes back to where the person came from,
      // so the code in it is not left in the history.
      const user = await userManager.signinCallback();
      const returnTo = (user?.state as { returnTo?: string } | undefined)?.returnTo ?? '/';
      history.replaceState(null, '', returnTo);
      return route(returnTo);
    }
    case '/signout-callback':
      await userManager.signoutCallback();
      history.replaceState(null, '', '/');
      return route('/');
    default:
      return notFoundPage();
  }
}

async function main() {
  const config = await loadConfig();
  const missing = [
    ...(config.issuer ? [] : ['UMBRAL_ISSUER']),
    ...(config.clientId ? [] : ['UMBRAL_CLIENT_ID']),
  ];
  if (missing.length > 0) {
    notConfiguredPage(missing);
    return;
  }
  userManager = createUserManager(config);
  await route(window.location.pathname);
}

main().catch(failurePage);