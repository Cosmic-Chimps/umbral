### Added

- "Sign in with Microsoft": a built-in Microsoft connector. You register an app in the Microsoft
  Entra admin center, enter its client ID and client secret, and choose who may sign in:
  personal Microsoft accounts, the people of one organization (by its tenant ID), any work or
  school account, or both kinds. Umbral checks each person's tenant against that choice, and
  checks the identity token's issuer against exactly the tenant it names. People are identified by
  their tenant and account together, so replacing the app registration keeps everyone. An email
  address counts as verified only when Microsoft's optional claim `xms_edov` says so, which you
  add in the registration; without it no address is verified. Narrowing the choice turns away people no
  longer admitted the next time an app authorizes them or refreshes their token. Microsoft client secrets expire (180
  days is the recommended lifetime, 2 years the longest): the walkthrough says how to renew one.
  There is one Microsoft connector per instance, no avatar, and the button carries no Microsoft
  logo yet. The one-tenant case can stay on the OpenID Connect connector. The walkthrough is
  `docs/connectors/microsoft.md`.

### Upgrade

- The database becomes schema version 13, which adds one nullable column. It is automatic and
  needs no action. An older image refuses to start on it: take a backup before you upgrade.
