### Added

- A desktop app can sign in through a loopback redirect on any free port, as RFC 8252 §7.3 asks.
  Register the address with no port, such as `http://127.0.0.1/callback` or
  `http://[::1]/callback`, on a **Single-page or native app**, and the app may send it with any
  port. Only `http`, only those two addresses (never `localhost`), and the path and query must
  match exactly. A loopback address registered with a port still accepts only that port, and the
  post-logout redirect URI follows the same rule. See "Desktop apps" in `docs/native-apps.md`.

### Security

- A **Single-page or native app** registered with a loopback address and no port accepted that
  address on any other port, also for `localhost` and `https://` addresses, and for sign-out as
  well as sign-in. The OpenIddict library behind Umbral did this by default, while the
  documentation said the port had to match. Umbral now applies only the narrower rule above. If an
  app registered `http://localhost/...` or `https://127.0.0.1/...` without a port and relied on
  another port, register `http://127.0.0.1/...` instead.
