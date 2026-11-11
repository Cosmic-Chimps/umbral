# Forgotten operator credential

Run this on the machine that hosts the container:

```bash
docker exec -it umbral /app/Umbral reset-credential
# compose:
docker compose exec umbral /app/Umbral reset-credential
```

It replaces the operator password with a freshly generated one and prints it,
once. It also turns password sign-in back on and signs out every admin session.
It keeps the operator's passkeys. You see:

```
================================================================
  The operator password has been replaced.

  New password     <generated>

  Password sign-in is on.
  2 operator passkeys are kept. Add --forget-passkeys to remove them.

  Sign in with it, then change it from the admin area.
  This is the only time it is shown.
================================================================
```

Sign in at `/admin/signin` with it, then choose your own under **Credential**,
and store it in a password manager. It is also the way back in when password
sign-in was off and you lost every device that holds a passkey.

If a device was stolen, also remove the operator passkeys:

```bash
docker exec -it umbral /app/Umbral reset-credential --forget-passkeys
# compose:
docker compose exec umbral /app/Umbral reset-credential --forget-passkeys
```

The line about passkeys then reads `2 operator passkeys were removed.` With one
passkey it reads `1 operator passkey is kept.` or `1 operator passkey was
removed.`, and with none the line is not printed. Add a passkey again under
**Credential** afterwards.

There is deliberately no "forgot your credential" page: reaching this command
needs access to the machine, which is more than reaching the web page needs.
