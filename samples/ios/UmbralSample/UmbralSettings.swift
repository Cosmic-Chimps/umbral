// The two settings of the sample. They are built into the app, so build again after changing
// them. There is no secret (a public client: PKCE proves it is the app) and no port.

// swiftlint:disable identifier_name
enum UmbralSettings {
    /// The Issuer URL on the app's page in Umbral.
    static let UMBRAL_ISSUER = "http://localhost:8080"
    /// The app's Client ID. There is no secret: a public client.
    static let UMBRAL_CLIENT_ID = ""
}
// swiftlint:enable identifier_name
