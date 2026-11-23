### Changed

- The OpenID Connect guide names Forgejo's fix for the bug that let an account make an address it
  never confirmed its primary one and have Forgejo send it as verified: Forgejo 15.0.12 and 17.0.2
  (2026-11-18, security advisory #63). A Forgejo you run on 15.0.12 or a later v15, or on 17.0.2 or
  later, with registration closed or confirming addresses, can be set to **Trusted**. On any other
  version, v16 included, keep it **Not trusted**, and keep Codeberg **Not trusted** until you have
  checked that the build it runs carries the fix.
