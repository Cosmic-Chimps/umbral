### Security

- A person's session or refresh token is honoured only while the connector they signed in
  through still exists and is of the same provider. Before, a refresh through a deleted
  connector went on working with none of that connector's limits (its allowed email domains,
  servers or Microsoft tenants), and a new connector given an old connector's name judged the
  old sign-ins by its own rules. Now a session whose connector's name belongs to another
  provider is signed out, and a refresh through a deleted or replaced connector answers
  `invalid_grant`; the person signs in again and keeps the same account. Adding a deleted
  connector back under the same name and provider lets its tokens refresh again. The security
  log records `connector_changed;reason=deleted` or `reason=other_kind`.
