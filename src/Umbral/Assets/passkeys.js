// The WebAuthn ceremony script (specs/008-passkey-connector, contracts/ceremonies.md
// "The script"). It is linked only on the three pages that run a ceremony: the
// passkey sign-in page, the invitation page and the passkeys page. Plain
// JavaScript, no framework, no build step, like umbral.js.
//
// It is a file, served from this origin, because the pages' Content-Security-Policy
// (Views/Layout.fs) forbids inline script. Views mark elements with data-* attributes
// and the listeners below act on them:
//
//   [data-passkey-signin]      a button: sign in with a passkey
//   [data-passkey-signup]      a form: create an account with a passkey (open sign-up)
//   [data-passkey-invitation]  a form: redeem the invitation link in the address
//   [data-passkey-add]         a form: add a passkey to the signed-in account
//
// It says nothing of its own. Every word the person reads is already in the page,
// and a refusal is shown by asking the server for the page again with the notice
// code the server named (`?msg=<code>`), so no text lives here and the script
// cannot disagree with Texts.fs. What it sends the server is the browser's own
// answer, untouched: Umbral never trusts it, and fido2-net-lib verifies it.

// A person who dismissed the browser's passkey dialog has not failed anything:
// the page is left as it was.
var PASSKEY_CANCELLED = ['NotAllowedError', 'AbortError'];

// The notice shown when the browser, not the server, could not finish. Codes, not
// text; the page may name another with data-failure on the element.
var PASSKEY_SIGNIN_FAILED = 'passkey-sign-in-failed';
var PASSKEY_REGISTRATION_FAILED = 'passkey-registration-failed';

// A server refusal: the notice code it sent. Distinct from any other exception so
// the person is told what the server said and not what the browser did.
function PasskeyRefusal(code) {
  this.code = code;
}

function base64UrlToBuffer(text) {
  var padded = text.replace(/-/g, '+').replace(/_/g, '/');
  while (padded.length % 4 !== 0) padded += '=';
  var binary = atob(padded);
  var bytes = new Uint8Array(binary.length);
  for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function bufferToBase64Url(buffer) {
  var bytes = new Uint8Array(buffer);
  var binary = '';
  for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// The credential descriptors of a list (excludeCredentials, allowCredentials), with
// their ids as buffers.
function passkeyDescriptors(list) {
  return (list || []).map(function (descriptor) {
    var copy = {};
    Object.keys(descriptor).forEach(function (key) { copy[key] = descriptor[key]; });
    copy.id = base64UrlToBuffer(descriptor.id);
    return copy;
  });
}

// The options the server sent (the library's JSON, with base64url strings) as the
// object `navigator.credentials` takes. The browser's own parser when it has one;
// otherwise the same conversion by hand.
function parseCreationOptions(options) {
  if (window.PublicKeyCredential && typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function') {
    return PublicKeyCredential.parseCreationOptionsFromJSON(options);
  }
  var copy = {};
  Object.keys(options).forEach(function (key) { copy[key] = options[key]; });
  copy.challenge = base64UrlToBuffer(options.challenge);
  copy.user = {
    id: base64UrlToBuffer(options.user.id),
    name: options.user.name,
    displayName: options.user.displayName
  };
  copy.excludeCredentials = passkeyDescriptors(options.excludeCredentials);
  return copy;
}

function parseRequestOptions(options) {
  if (window.PublicKeyCredential && typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function') {
    return PublicKeyCredential.parseRequestOptionsFromJSON(options);
  }
  var copy = {};
  Object.keys(options).forEach(function (key) { copy[key] = options[key]; });
  copy.challenge = base64UrlToBuffer(options.challenge);
  copy.allowCredentials = passkeyDescriptors(options.allowCredentials);
  return copy;
}

// What the browser made, as the JSON the server reads: `toJSON()` when there is
// one, otherwise the same shape by hand. The server requires `clientExtensionResults`
// and, on a registration, `response.transports`, so they are always present: empty
// when the browser cannot say.
function credentialToJson(credential) {
  if (typeof credential.toJSON === 'function') return credential.toJSON();

  var response = credential.response;
  var json = {
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    clientExtensionResults: typeof credential.getClientExtensionResults === 'function'
      ? credential.getClientExtensionResults()
      : {},
    response: { clientDataJSON: bufferToBase64Url(response.clientDataJSON) }
  };
  if (credential.authenticatorAttachment) json.authenticatorAttachment = credential.authenticatorAttachment;

  if (response.attestationObject) {
    json.response.attestationObject = bufferToBase64Url(response.attestationObject);
    json.response.transports = typeof response.getTransports === 'function' ? response.getTransports() : [];
  } else {
    json.response.authenticatorData = bufferToBase64Url(response.authenticatorData);
    json.response.signature = bufferToBase64Url(response.signature);
    json.response.userHandle = response.userHandle ? bufferToBase64Url(response.userHandle) : null;
  }
  return json;
}

// The antiforgery token Umbral rendered into the page for this purpose, sent as
// the header ASP.NET Core's antiforgery looks for.
function antiforgeryToken() {
  var field = document.querySelector('input[name=__RequestVerificationToken]');
  return field ? field.value : '';
}

// POST JSON, and read the answer: `{ ceremony, options }`, `{ redirect }`, or a
// refusal `{ error }`, which throws a PasskeyRefusal. Anything else (the
// antiforgery page, a server error) is a failure the page names itself.
function postPasskeyJson(url, body) {
  var headers = { 'Content-Type': 'application/json' };
  var token = antiforgeryToken();
  if (token) headers['RequestVerificationToken'] = token;

  return fetch(url, { method: 'POST', headers: headers, credentials: 'same-origin', body: JSON.stringify(body) })
    .then(function (response) {
      return response.json().then(
        function (data) { return { ok: response.ok, data: data }; },
        function () { return { ok: response.ok, data: null }; }
      );
    })
    .then(function (result) {
      if (result.ok && result.data) return result.data;
      if (result.data && typeof result.data.error === 'string') throw new PasskeyRefusal(result.data.error);
      throw new Error('unexpected-response');
    });
}

// The page again, with the notice the server named. Everything else in the
// address stays (`returnUrl`, the invitation's own path).
function showPasskeyNotice(code) {
  var address = new URL(window.location.href);
  address.searchParams.set('msg', code);
  window.location.assign(address.toString());
}

// Only a path of this site: the server sanitises what it sends, and this is the
// second look before the browser follows it.
function followPasskeyRedirect(path) {
  if (typeof path !== 'string' || path.charAt(0) !== '/' || path.charAt(1) === '/' || path.charAt(1) === '\\') {
    throw new Error('unexpected-redirect');
  }
  window.location.assign(path);
}

// The `returnUrl` of the page, which the server sanitises again.
function passkeyReturnUrl() {
  return new URL(window.location.href).searchParams.get('returnUrl') || '';
}

// The secret of an invitation link, which is the last segment of the page's own
// address (`/invite/<secret>`), so it is never written into the page.
function passkeyInvitationSecret() {
  var segments = window.location.pathname.split('/');
  return decodeURIComponent(segments[segments.length - 1] || '');
}

function passkeyField(form, name) {
  var field = form.querySelector('[name="' + name + '"]');
  return field ? field.value : '';
}

// Runs one ceremony from a control and the first request's body. `kind` is
// 'create' (registration) or 'get' (sign-in). While it runs the control is
// disabled, so a second click cannot start a second ceremony; a cancelled
// dialog enables it again and says nothing.
function runPasskeyCeremony(control, kind, optionsUrl, finishUrl, body) {
  var failure = control.getAttribute('data-failure')
    || (kind === 'get' ? PASSKEY_SIGNIN_FAILED : PASSKEY_REGISTRATION_FAILED);

  if (!window.PublicKeyCredential || !navigator.credentials) {
    showPasskeyNotice(failure);
    return;
  }

  control.disabled = true;

  postPasskeyJson(optionsUrl, body)
    .then(function (started) {
      var prompt = kind === 'get'
        ? navigator.credentials.get({ publicKey: parseRequestOptions(started.options) })
        : navigator.credentials.create({ publicKey: parseCreationOptions(started.options) });
      return prompt.then(function (credential) {
        if (!credential) throw new Error('no-credential');
        return postPasskeyJson(finishUrl, { ceremony: started.ceremony, credential: credentialToJson(credential) });
      });
    })
    .then(function (done) {
      followPasskeyRedirect(done.redirect);
    })
    .catch(function (error) {
      if (error instanceof PasskeyRefusal) {
        showPasskeyNotice(error.code);
      } else if (error && PASSKEY_CANCELLED.indexOf(error.name) >= 0) {
        control.disabled = false;
      } else {
        showPasskeyNotice(failure);
      }
    });
}

// Sign in: nobody is asked who they are. The authenticator offers the passkeys it
// holds for this site, and the answer carries the user handle.
document.addEventListener('click', function (event) {
  if (!(event.target instanceof Element)) return;
  var button = event.target.closest('[data-passkey-signin]');
  if (!button || button.disabled) return;
  event.preventDefault();
  runPasskeyCeremony(button, 'get', '/passkey/assertion/options', '/passkey/assertion', {
    returnUrl: passkeyReturnUrl()
  });
});

// The three registration forms. Each names its mode, and the request carries only
// what the person typed into it.
document.addEventListener('submit', function (event) {
  var form = event.target;
  if (!(form instanceof HTMLFormElement)) return;

  var body;
  if (form.hasAttribute('data-passkey-signup')) {
    body = {
      mode: 'signup',
      displayName: passkeyField(form, 'displayName'),
      nickname: passkeyField(form, 'nickname'),
      returnUrl: passkeyReturnUrl()
    };
  } else if (form.hasAttribute('data-passkey-invitation')) {
    body = {
      mode: 'invitation',
      invitation: passkeyInvitationSecret(),
      displayName: passkeyField(form, 'displayName'),
      nickname: passkeyField(form, 'nickname')
    };
  } else if (form.hasAttribute('data-passkey-add')) {
    body = { mode: 'add', nickname: passkeyField(form, 'nickname') };
  } else {
    return;
  }

  event.preventDefault();
  var submit = form.querySelector('button[type="submit"],input[type="submit"]') || form;
  runPasskeyCeremony(submit, 'create', '/passkey/registration/options', '/passkey/registration', body);
});
