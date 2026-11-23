### Fixed

- An idle instance holds about 2 MiB less memory again. Since 0.12.0, every start ran the
  declaration step even with no `UMBRAL__DECLARATION` set and nothing declared, and preparing it
  left memory in use for as long as the instance ran. A start with nothing to declare now skips it.
