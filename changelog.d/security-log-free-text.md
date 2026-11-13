### Changed

- In the security log, a reason that comes from outside Umbral (a provider's answer, a network
  error, a failed check when a connector is saved) can no longer look like extra fields of the
  detail: its `;` is written as `,`, its `=` as `:`, line breaks as spaces, and it is cut at 300
  characters.
