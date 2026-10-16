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

// The logo form on an app's card (`[data-logo-form]`): choose an image, crop it,
// and send Umbral a PNG it will accept (specs/006-app-signin-branding research
// R1 and the "Crop script" of contracts/admin-branding.md).
//
// The browser does the image work so that the server never decodes one: it
// checks the file, shows it with a crop rectangle, and on submit draws the crop
// onto a canvas no larger than the stored limit, exports it as a PNG and puts
// that PNG in the file input in place of the chosen file. The server then
// checks that PNG strictly all the same, because nothing it receives is trusted
// to have come from here. Without this script, or in a browser that lacks
// `canvas.toBlob` or `DataTransfer`, the original file is sent as it is and the
// server accepts a PNG that already meets its limits.
//
// The form tells the script everything it needs to say or to check, as data-*
// attributes (the limits are the server's numbers; the texts are Texts.fs), so
// the script holds no text of its own. Its positions are set from script
// (`element.style.left = ...`), which the Content-Security-Policy allows, where
// a `style=""` in markup would be refused.
//
// Not htmx: nothing is requested until the form is submitted, and what is
// submitted is a file this script made.

// What each form is currently doing, by form element.
var logoStates = new WeakMap();

function logoAttr(form, name) {
  return form.getAttribute('data-' + name) || '';
}

function logoNumber(form, name) {
  return parseInt(logoAttr(form, name), 10);
}

function logoParts(form) {
  return {
    input: form.querySelector('[data-logo-input]'),
    crop: form.querySelector('[data-logo-crop]'),
    error: form.querySelector('[data-logo-error]'),
    whole: form.querySelector('[data-logo-whole]')
  };
}

// An error line from the form's own text (`data-` + attribute), with `{name}`
// filled in from values.
function showLogoError(form, attribute, values) {
  var parts = logoParts(form);
  var text = logoAttr(form, attribute);
  Object.keys(values || {}).forEach(function (key) {
    text = text.split('{' + key + '}').join(values[key]);
  });
  parts.error.textContent = text;
  parts.error.hidden = false;
}

function clearLogoError(form) {
  var parts = logoParts(form);
  parts.error.textContent = '';
  parts.error.hidden = true;
}

// Forget the chosen image: the crop panel, the buttons, any error.
function resetLogoForm(form) {
  var parts = logoParts(form);
  logoStates.delete(form);
  parts.crop.textContent = '';
  parts.whole.hidden = true;
}

// The first bytes of the file say what it is. Returns a name, or '' for a format
// the script does not take. The browser's own MIME type is the file's extension
// at best, so it is not asked.
function logoFormatOf(bytes) {
  function at(i) { return bytes[i]; }
  if (at(0) === 0x89 && at(1) === 0x50 && at(2) === 0x4e && at(3) === 0x47 && at(4) === 0x0d && at(5) === 0x0a && at(6) === 0x1a && at(7) === 0x0a) return 'png';
  if (at(0) === 0xff && at(1) === 0xd8 && at(2) === 0xff) return 'jpeg';
  if (at(0) === 0x52 && at(1) === 0x49 && at(2) === 0x46 && at(3) === 0x46 && at(8) === 0x57 && at(9) === 0x45 && at(10) === 0x42 && at(11) === 0x50) return 'webp';
  return '';
}

// A crop that survives being scaled down to the stored limit: neither side may
// end up under the minimum. A 4000 x 100 strip scaled to 512 wide would be 13
// high, which the server refuses, so such a strip cannot be chosen.
function logoCropIsUsable(form, width, height) {
  var min = logoNumber(form, 'min-side');
  var store = logoNumber(form, 'store-side');
  var scale = Math.min(1, store / Math.max(width, height));
  return width >= min && height >= min && Math.min(width, height) * scale >= min;
}

// The largest crop that is the whole image where that is usable, and otherwise
// the whole of the short side and as much of the long side as can be used,
// centred.
function logoWholeCrop(form, state) {
  var w = state.width;
  var h = state.height;
  var store = logoNumber(form, 'store-side');
  var min = logoNumber(form, 'min-side');

  if (logoCropIsUsable(form, w, h)) return { x: 0, y: 0, w: w, h: h };

  // Only a long strip is unusable, and the longest side that works is the one
  // whose scaled short side is exactly the minimum: long = short * store / min.
  var ratio = store / min;
  if (w >= h) {
    var longW = Math.min(w, Math.floor(h * ratio));
    return { x: Math.floor((w - longW) / 2), y: 0, w: longW, h: h };
  }
  var longH = Math.min(h, Math.floor(w * ratio));
  return { x: 0, y: Math.floor((h - longH) / 2), w: w, h: longH };
}

// Place the crop rectangle over the scaled image.
function renderLogoCrop(state) {
  var s = state.scale;
  state.box.style.left = (state.crop.x * s) + 'px';
  state.box.style.top = (state.crop.y * s) + 'px';
  state.box.style.width = (state.crop.w * s) + 'px';
  state.box.style.height = (state.crop.h * s) + 'px';
}

// Accept a new crop if it is inside the image and usable; otherwise keep the old one.
function setLogoCrop(form, state, next) {
  var inside = next.x >= 0 && next.y >= 0 && next.x + next.w <= state.width && next.y + next.h <= state.height;
  if (!inside || !logoCropIsUsable(form, next.w, next.h)) return false;
  state.crop = next;
  renderLogoCrop(state);
  return true;
}

function clampNumber(value, low, high) {
  return Math.max(low, Math.min(high, value));
}

// Build the crop panel for a loaded image: the picture, the rectangle and its
// four corner handles, with pointer and keyboard control.
function buildLogoCropper(form, state, source) {
  var parts = logoParts(form);

  var stage = document.createElement('div');
  stage.className = 'um-crop-stage';

  var image = document.createElement('img');
  image.className = 'um-crop-image';
  image.alt = '';
  image.draggable = false;
  image.src = source;

  var box = document.createElement('div');
  box.className = 'um-crop-box';
  box.tabIndex = 0;
  box.setAttribute('role', 'group');
  box.setAttribute('aria-label', logoAttr(form, 'label-crop'));

  ['nw', 'ne', 'sw', 'se'].forEach(function (corner) {
    var handle = document.createElement('span');
    handle.className = 'um-crop-handle um-crop-handle-' + corner;
    handle.setAttribute('data-corner', corner);
    handle.title = logoAttr(form, 'label-handle');
    box.appendChild(handle);
  });

  stage.appendChild(image);
  stage.appendChild(box);
  parts.crop.appendChild(stage);

  // Fit the picture to the panel, never larger than itself and never taller
  // than a screenful of form.
  var available = Math.max(parts.crop.clientWidth, 160);
  state.scale = Math.min(1, available / state.width, 360 / state.height);
  image.style.width = (state.width * state.scale) + 'px';
  image.style.height = (state.height * state.scale) + 'px';
  stage.style.width = (state.width * state.scale) + 'px';
  stage.style.height = (state.height * state.scale) + 'px';

  state.stage = stage;
  state.box = box;
  state.crop = logoWholeCrop(form, state);
  renderLogoCrop(state);

  var drag = null;

  // A pointer position in source pixels, relative to the picture.
  function sourcePoint(event) {
    var rect = stage.getBoundingClientRect();
    return {
      x: clampNumber((event.clientX - rect.left) / state.scale, 0, state.width),
      y: clampNumber((event.clientY - rect.top) / state.scale, 0, state.height)
    };
  }

  box.addEventListener('pointerdown', function (event) {
    if (event.button !== undefined && event.button !== 0) return;
    var corner = event.target.getAttribute && event.target.getAttribute('data-corner');
    var point = sourcePoint(event);
    drag = {
      corner: corner,
      start: point,
      crop: { x: state.crop.x, y: state.crop.y, w: state.crop.w, h: state.crop.h }
    };
    box.setPointerCapture(event.pointerId);
    box.focus();
    event.preventDefault();
  });

  box.addEventListener('pointermove', function (event) {
    if (!drag) return;
    var point = sourcePoint(event);
    var min = logoNumber(form, 'min-side');
    var start = drag.crop;

    if (!drag.corner) {
      // Move: by how far the pointer has gone, kept inside the picture.
      setLogoCrop(form, state, {
        x: Math.round(clampNumber(start.x + (point.x - drag.start.x), 0, state.width - start.w)),
        y: Math.round(clampNumber(start.y + (point.y - drag.start.y), 0, state.height - start.h)),
        w: start.w,
        h: start.h
      });
      return;
    }

    // Resize: the corner opposite the one dragged stays where it was, and the
    // dragged one follows the pointer, but not closer than the minimum.
    var anchorX = drag.corner.indexOf('w') >= 0 ? start.x + start.w : start.x;
    var anchorY = drag.corner.indexOf('n') >= 0 ? start.y + start.h : start.y;
    var targetX = point.x;
    var targetY = point.y;
    if (Math.abs(targetX - anchorX) < min) targetX = anchorX + (targetX >= anchorX ? min : -min);
    if (Math.abs(targetY - anchorY) < min) targetY = anchorY + (targetY >= anchorY ? min : -min);
    // The minimum, pushed past the edge of the picture, goes the other way.
    if (targetX < 0 || targetX > state.width) targetX = anchorX + (targetX < 0 ? min : -min);
    if (targetY < 0 || targetY > state.height) targetY = anchorY + (targetY < 0 ? min : -min);

    setLogoCrop(form, state, {
      x: Math.round(Math.min(anchorX, targetX)),
      y: Math.round(Math.min(anchorY, targetY)),
      w: Math.round(Math.abs(targetX - anchorX)),
      h: Math.round(Math.abs(targetY - anchorY))
    });
  });

  function endDrag(event) {
    if (!drag) return;
    drag = null;
    if (box.hasPointerCapture && box.hasPointerCapture(event.pointerId)) box.releasePointerCapture(event.pointerId);
  }
  box.addEventListener('pointerup', endDrag);
  box.addEventListener('pointercancel', endDrag);

  // Arrow keys move the rectangle by one pixel of the picture as it is shown
  // (at least one source pixel); with Shift they resize it, right and down
  // growing it and left and up shrinking it.
  box.addEventListener('keydown', function (event) {
    var keys = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    var direction = keys[event.key];
    if (!direction) return;
    var step = Math.max(1, Math.round(1 / state.scale));
    var c = state.crop;

    if (event.shiftKey) {
      setLogoCrop(form, state, { x: c.x, y: c.y, w: c.w + direction[0] * step, h: c.h + direction[1] * step });
    } else {
      setLogoCrop(form, state, {
        x: clampNumber(c.x + direction[0] * step, 0, state.width - c.w),
        y: clampNumber(c.y + direction[1] * step, 0, state.height - c.h),
        w: c.w,
        h: c.h
      });
    }
    event.preventDefault();
  });
}

// The file the operator chose: checked in the order the contract gives, so the
// first thing wrong is the thing said, and nothing is sent that fails a check.
document.addEventListener('change', function (event) {
  var input = event.target;
  if (!(input instanceof HTMLInputElement) || !input.hasAttribute('data-logo-input')) return;
  var form = input.closest('[data-logo-form]');
  if (!form) return;

  resetLogoForm(form);
  clearLogoError(form);
  var file = input.files && input.files[0];
  if (!file) return;

  function refuse(attribute, values) {
    input.value = '';
    resetLogoForm(form);
    showLogoError(form, attribute, values);
  }

  var maxBytes = logoNumber(form, 'max-file-bytes');
  if (file.size > maxBytes) {
    refuse('error-file-size', { actual: (file.size / 1048576).toFixed(1), limit: String(maxBytes / 1048576) });
    return;
  }

  var head = new FileReader();
  head.onload = function () {
    if (!logoFormatOf(new Uint8Array(head.result))) {
      refuse('error-format');
      return;
    }

    var reader = new FileReader();
    reader.onload = function () {
      var picture = new Image();
      picture.onload = function () {
        var limit = logoNumber(form, 'max-source-side');
        if (picture.naturalWidth > limit || picture.naturalHeight > limit) {
          refuse('error-dimensions', { width: String(picture.naturalWidth), height: String(picture.naturalHeight), limit: String(limit) });
          return;
        }

        var min = logoNumber(form, 'min-side');
        var state = { picture: picture, width: picture.naturalWidth, height: picture.naturalHeight, file: file, converted: false };
        logoStates.set(form, state);

        // An image under the minimum cannot be cropped to a usable logo. It is
        // sent whole, as a PNG, so the server says how small it is.
        if (state.width < min || state.height < min) {
          state.crop = { x: 0, y: 0, w: state.width, h: state.height };
          return;
        }

        buildLogoCropper(form, state, reader.result);
        logoParts(form).whole.hidden = false;
      };
      picture.onerror = function () { refuse('error-not-image'); };
      picture.src = reader.result;
    };
    reader.onerror = function () { refuse('error-not-image'); };
    reader.readAsDataURL(file);
  };
  head.onerror = function () { refuse('error-not-image'); };
  head.readAsArrayBuffer(file.slice(0, 12));
});

// "Use whole image" puts the rectangle back over all of it.
document.addEventListener('click', function (event) {
  if (!(event.target instanceof Element)) return;
  var button = event.target.closest('[data-logo-whole]');
  if (!button) return;
  var form = button.closest('[data-logo-form]');
  var state = form && logoStates.get(form);
  if (!state || !state.box) return;
  setLogoCrop(form, state, logoWholeCrop(form, state));
});

// On submit, the crop becomes the file. The first submit is held while the
// canvas is exported; the form is then submitted again with the PNG in place,
// and that second submit goes through untouched. Anything that stops the
// conversion (an API the browser lacks, a canvas that will not export) leaves
// the original file in the input and lets the form go as it was.
document.addEventListener('submit', function (event) {
  var form = event.target;
  if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-logo-form')) return;
  var state = logoStates.get(form);
  var parts = logoParts(form);
  if (!state || state.converted || !parts.input.files || !parts.input.files.length) return;

  var canCreateFile = typeof DataTransfer === 'function' && typeof File === 'function';
  var canvas = document.createElement('canvas');
  if (!canCreateFile || typeof canvas.toBlob !== 'function') return;

  // Whole image when there was no rectangle (an image under the minimum).
  var crop = state.crop;
  var store = logoNumber(form, 'store-side');
  var shrink = Math.min(1, store / Math.max(crop.w, crop.h));
  canvas.width = Math.max(1, Math.round(crop.w * shrink));
  canvas.height = Math.max(1, Math.round(crop.h * shrink));

  var context = canvas.getContext('2d');
  if (!context) return;

  event.preventDefault();
  event.stopPropagation();
  context.drawImage(state.picture, crop.x, crop.y, crop.w, crop.h, 0, 0, canvas.width, canvas.height);

  function send() {
    state.converted = true;
    if (typeof form.requestSubmit === 'function') form.requestSubmit(); else form.submit();
  }

  canvas.toBlob(function (blob) {
    try {
      if (blob) {
        var transfer = new DataTransfer();
        transfer.items.add(new File([blob], 'logo.png', { type: 'image/png' }));
        parts.input.files = transfer.files;
      }
    } catch (failure) {
      // Keep the original file: the server will say what is wrong with it.
    }
    send();
  }, 'image/png');
}, true);
