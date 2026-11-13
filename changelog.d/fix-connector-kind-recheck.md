### Security

- A person's session or refresh token is honoured only while the connector they signed in
  through still exists and is of the same provider. Before, a refresh through a deleted
  connector went on working with none of that connector's limits (its allowed email domains,
  servers or Microsoft tenants), and a new connector given an old connector's name judged the
  old sign-ins by its own rules. Now a session whose connector's name belongs to another
  provider is signed out, and a refresh through a deleted connector, or one whose name now
  belongs to a connector of another provider, answers `invalid_grant`; the person signs in
  again. A connector re-created under the same name and provider is treated as the same
  connector: sessions and refresh tokens from before go on, judged by its settings. Disabling a
  connector changes nothing here: its tokens still refresh. The security log records
  `connector_changed;reason=deleted`, `reason=other_kind`, or `reason=unrecorded` for a token
  that names no connector (no released version issued one).
