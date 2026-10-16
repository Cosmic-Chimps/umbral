// The only script Umbral ships besides htmx. Plain JavaScript: Umbral loads no
// client framework, only htmx for the three interactions research.md D11 names,
// and neither of the two behaviours below is one of them.
//
// It is a file, served from this origin, because the pages' Content-Security-Policy
// (Views/Layout.fs) forbids inline script and inline event handlers. Views mark
// elements with data-* attributes and the listeners below act on them.

// Wires `navigator.clipboard` onto every `[data-copy-trigger]` button,
// copying the value of the `<input>` next to it and swapping the button's
// label for the "copied" one for two seconds.
document.addEventListener('click', function (event) {
  var trigger = event.target.closest('[data-copy-trigger]');
  if (!trigger) return;
  var field = trigger.closest('div');
  var source = field ? field.querySelector('input') : null;
  if (!source || !navigator.clipboard) return;
  navigator.clipboard.writeText(source.value).then(function () {
    var idle = trigger.querySelector('[data-copy-label-idle]');
    var done = trigger.querySelector('[data-copy-label-done]');
    if (!idle || !done) return;
    idle.classList.add('hidden');
    done.classList.remove('hidden');
    setTimeout(function () {
      idle.classList.remove('hidden');
      done.classList.add('hidden');
    }, 2000);
  });
});

// A native confirm() on every form carrying `data-confirm`, so a destructive
// action names what breaks before it goes through. Both listeners use the
// capture phase on the document, which runs before any listener htmx attaches
// to the element itself, so stopPropagation keeps htmx from issuing the
// request; delegation also covers forms that htmx swaps in later.
function confirmed(form) {
  return window.confirm(form.getAttribute('data-confirm'));
}

// A plain form: the confirm runs on its submit event.
document.addEventListener('submit', function (event) {
  var form = event.target;
  if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-confirm')) return;
  if (!confirmed(form)) {
    event.preventDefault();
    event.stopPropagation();
  }
}, true);

// A submit button carrying hx-post (the connector and user toggles): htmx 4
// binds it to `click` and cancels the click's default, so no submit event ever
// fires and the listener above would never see it. Without htmx loaded the
// click submits natively and the listener above confirms, so this one stands
// aside rather than ask twice.
document.addEventListener('click', function (event) {
  if (!window.htmx || !(event.target instanceof Element)) return;
  var button = event.target.closest('button[hx-post], input[type="submit"][hx-post]');
  var form = button ? button.form : null;
  if (!form || !form.hasAttribute('data-confirm')) return;
  if (!confirmed(form)) {
    event.preventDefault();
    event.stopPropagation();
  }
}, true);

// The accent colour form on an app's card (`[data-accent-form]`): a text field
// for `#rrggbb` and a native colour picker beside it, kept in step both ways.
//
// The text field is the form; the picker is a convenience and has no `name`, so
// it is never posted. That is why it exists only when this script runs: the
// server renders it `hidden`, and it is shown here, because a picker that does
// nothing without the script is worse than none, and because a colour input
// cannot be empty, so it could never say "no colour". Not htmx: nothing is
// requested, two inputs are only copied into each other.
var ACCENT_HEX = /^#[0-9a-fA-F]{6}$/;

function accentParts(form) {
  return {
    text: form.querySelector('input[name="accent"]'),
    picker: form.querySelector('[data-accent-picker]')
  };
}

// The picker follows the text field, but only for a value the server would
// accept; a half-typed colour leaves the picker where it was.
function pickerFollowsText(parts) {
  if (parts.text && parts.picker && ACCENT_HEX.test(parts.text.value.trim())) {
    parts.picker.value = parts.text.value.trim().toLowerCase();
  }
}

function startAccentForms() {
  document.querySelectorAll('[data-accent-form]').forEach(function (form) {
    var parts = accentParts(form);
    if (!parts.picker) return;
    parts.picker.hidden = false;
    pickerFollowsText(parts);
  });
}

// This file loads from <head>, before the forms exist.
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startAccentForms);
} else {
  startAccentForms();
}

document.addEventListener('input', function (event) {
  if (!(event.target instanceof Element)) return;
  var form = event.target.closest('[data-accent-form]');
  if (!form) return;
  var parts = accentParts(form);

  if (event.target === parts.picker && parts.text) {
    // A colour input's value is always lowercase `#rrggbb`.
    parts.text.value = parts.picker.value.toLowerCase();
  } else if (event.target === parts.text) {
    pickerFollowsText(parts);
  }
});
