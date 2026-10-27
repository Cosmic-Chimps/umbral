// The WebAuthn ceremony script (specs/008-passkey-connector, contracts/ceremonies.md
// "The script"; specs/011-operator-passkeys, contracts/routes.md "Script hooks").
// It is linked only on the pages that run a ceremony: the passkey sign-in page, the
// invitation page and the passkeys page of the connector, and the operator's sign-in
// and credential pages. Plain JavaScript, no framework, no build step, like umbral.js.
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
// The operator's own passkeys (admin pages, /admin/...):
//
//   [data-scripted]                  any element rendered `hidden` because it cannot
//                                    work without this script: revealed on load
//   [data-operator-passkey-signin]   a button: sign the operator in with a passkey
//   [data-operator-passkey-add]      a form: add a passkey to the operator; with a
//                                    password typed, the password re-checks it,
//                                    otherwise a passkey ceremony does
//   [data-operator-confirm]          a form (remove a passkey, the password sign-in
//                                    switch): a passkey ceremony re-checks it, and its
//                                    answer fills the hidden `ceremony` and
//                                    `credential` fields before the form is sent. The
//                                    attribute's value is the action the confirm is for
//                                    (`remove:<id>`, `switch-off`, `switch-on`)
//   [data-operator-confirm-passkey]  a submit button of such a form: always the
//                                    passkey ceremony, even when a password field exists
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
var OPERATOR_SIGNIN_FAILED = 'operator-passkey-signin-failed';
var OPERATOR_REGISTRATION_FAILED = 'operator-passkey-registration-failed';
var OPERATOR_NOT_CONFIRMED = 'operator-passkey-not-confirmed';

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
// dialog enables it again and says nothing. The notice for a failure of the browser
// is the element's data-failure, else `defaultFailure`, else the connector's.
function runPasskeyCeremony(control, kind, optionsUrl, finishUrl, body, defaultFailure) {
  var failure = control.getAttribute('data-failure')
    || defaultFailure
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

// The operator's pages. Elements that cannot work without this script are rendered
// `hidden` and `disabled` and carry data-scripted: they appear, usable, once it runs.
function revealScriptedControls() {
  var elements = document.querySelectorAll('[data-scripted]');
  for (var i = 0; i < elements.length; i++) {
    elements[i].hidden = false;
    elements[i].disabled = false;
  }
  // The browser skips `autofocus` on a control that was hidden and disabled
  // when the page loaded, so a confirm block's own button is focused here.
  for (var j = 0; j < elements.length; j++) {
    if (elements[j].hasAttribute('autofocus')) {
      elements[j].focus();
      break;
    }
  }
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', revealScriptedControls);
} else {
  revealScriptedControls();
}

document.addEventListener('click', function (event) {
  if (!(event.target instanceof Element)) return;
  var button = event.target.closest('[data-operator-passkey-signin]');
  if (!button || button.disabled) return;
  event.preventDefault();
  // The page names the notice for its own state (a password is or is not on offer).
  runPasskeyCeremony(button, 'get', '/admin/signin/passkey/options', '/admin/signin/passkey', {},
    button.getAttribute('data-operator-passkey-signin') || OPERATOR_SIGNIN_FAILED);
});

function passkeysUnavailable() {
  return !window.PublicKeyCredential || !navigator.credentials;
}

function submitButtonsOf(form) {
  return form.querySelectorAll('button[type="submit"],input[type="submit"],button:not([type])');
}

function setDisabled(controls, disabled) {
  for (var i = 0; i < controls.length; i++) controls[i].disabled = disabled;
}

// A form asks for a passkey when its submit button says so, or when it has no
// password field to type into. A password typed on the primary submit is the
// password's way.
function usesPasskeyConfirm(form, submitter) {
  return (!!submitter && submitter.hasAttribute('data-operator-confirm-passkey'))
    || !form.querySelector('input[name="current"]');
}

// The re-check by passkey: `{ ceremony, credential }` for the server to verify.
// No credential is a failure.
// `action` is what the confirm is for ('add', 'switch-off', 'switch-on' or
// 'remove:<id>'); the server accepts the answer for that action only.
function confirmWithPasskey(action) {
  return postPasskeyJson('/admin/credential/confirm/options', { action: action }).then(function (started) {
    return navigator.credentials.get({ publicKey: parseRequestOptions(started.options) })
      .then(function (credential) {
        if (!credential) throw new Error('no-credential');
        return { ceremony: started.ceremony, credential: credentialToJson(credential) };
      });
  });
}

// What an operator ceremony does with its error: the server's refusal names the
// notice, a cancelled dialog leaves the page as it was, anything else is `failure`.
function operatorPasskeyFailed(error, controls, failure) {
  if (error instanceof PasskeyRefusal) {
    showPasskeyNotice(error.code);
  } else if (error && PASSKEY_CANCELLED.indexOf(error.name) >= 0) {
    setDisabled(controls, false);
  } else {
    showPasskeyNotice(failure);
  }
}

// Add a passkey to the operator: the re-check (password or passkey) opens the
// registration, which then runs like the connector's.
document.addEventListener('submit', function (event) {
  var form = event.target;
  if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-operator-passkey-add')) return;

  event.preventDefault();
  if (passkeysUnavailable()) {
    showPasskeyNotice(OPERATOR_REGISTRATION_FAILED);
    return;
  }

  var controls = submitButtonsOf(form);
  setDisabled(controls, true);

  var name = passkeyField(form, 'name');
  var recheck = usesPasskeyConfirm(form, event.submitter)
    ? confirmWithPasskey('add').then(function (confirmed) {
      return { name: name, ceremony: confirmed.ceremony, credential: confirmed.credential };
    })
    : Promise.resolve({ name: name, current: passkeyField(form, 'current') });

  recheck
    .then(function (body) { return postPasskeyJson('/admin/credential/passkeys/options', body); })
    .then(function (started) {
      return navigator.credentials.create({ publicKey: parseCreationOptions(started.options) })
        .then(function (credential) {
          if (!credential) throw new Error('no-credential');
          return postPasskeyJson('/admin/credential/passkeys', {
            ceremony: started.ceremony,
            credential: credentialToJson(credential)
          });
        });
    })
    .then(function (done) { followPasskeyRedirect(done.redirect); })
    .catch(function (error) { operatorPasskeyFailed(error, controls, OPERATOR_REGISTRATION_FAILED); });
});

// The forms that need the operator's re-check (remove a passkey, the password
// sign-in switch). They post normally; the passkey's answer travels in their two
// hidden fields. `form.submit()` does not fire this event again.
document.addEventListener('submit', function (event) {
  var form = event.target;
  if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-operator-confirm')) return;
  if (!usesPasskeyConfirm(form, event.submitter)) return;

  event.preventDefault();
  if (passkeysUnavailable()) {
    showPasskeyNotice(OPERATOR_NOT_CONFIRMED);
    return;
  }

  var controls = submitButtonsOf(form);
  setDisabled(controls, true);

  confirmWithPasskey(form.getAttribute('data-operator-confirm'))
    .then(function (confirmed) {
      var ceremony = form.querySelector('input[name="ceremony"]');
      var credential = form.querySelector('input[name="credential"]');
      if (!ceremony || !credential) throw new Error('missing-field');
      ceremony.value = confirmed.ceremony;
      credential.value = JSON.stringify(confirmed.credential);
      var current = form.querySelector('input[name="current"]');
      if (current) current.value = '';
      form.submit();
    })
    .catch(function (error) { operatorPasskeyFailed(error, controls, OPERATOR_NOT_CONFIRMED); });
});
