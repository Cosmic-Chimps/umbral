### Changed

- When Umbral cannot reach a provider, the warning now gives the reason in one pair of brackets
  instead of brackets inside brackets: "Umbral could not reach Microsoft (the connection failed:
  Connection refused, 127.0.0.1:9)". The OpenID Connect provider warning puts its reason in
  brackets too, like the Apple and Microsoft ones.
