import Foundation
@preconcurrency import AppAuth
import Security

/// The tokens, kept in the Keychain (never UserDefaults): the whole `OIDAuthState`, archived, in one
/// generic-password item. `WhenUnlockedThisDeviceOnly` keeps it out of iCloud Keychain and out of
/// backups restored to another device.
enum TokenStore {
    private static let service = "com.cosmicchimps.umbral.sample"
    private static let account = "authState"

    private static var query: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }

    /// Returns `errSecSuccess`, or the status of the failed Keychain write (`errSecParam` when the
    /// state cannot be archived). On failure nothing is stored.
    @discardableResult
    static func save(_ state: OIDAuthState) -> OSStatus {
        clear()
        guard let data = try? NSKeyedArchiver.archivedData(withRootObject: state, requiringSecureCoding: true) else {
            return errSecParam
        }
        var item = query
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        return SecItemAdd(item as CFDictionary, nil)
    }

    /// Nil when there is nothing stored or it cannot be read; an unreadable item is deleted.
    static func load() -> OIDAuthState? {
        var request = query
        request[kSecReturnData as String] = true
        request[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(request as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess,
              let data = result as? Data,
              let state = try? NSKeyedUnarchiver.unarchivedObject(ofClass: OIDAuthState.self, from: data)
        else {
            clear()
            return nil
        }
        return state
    }

    static func clear() {
        SecItemDelete(query as CFDictionary)
    }
}
