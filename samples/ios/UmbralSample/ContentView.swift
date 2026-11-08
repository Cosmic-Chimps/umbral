import SwiftUI

/// The screens, like the pages of the web samples: Home, Public, and Private (the claims).
private enum Screen: Hashable { case `public`, `private` }

struct ContentView: View {
    let auth: UmbralAuth

    @State private var path: [Screen] = []
    @State private var message: String?
    // Nil means signed out. Re-read whenever the screen changes: the id_token may have expired.
    @State private var claims: [(name: String, value: String)]?

    private static let setClientIdMessage = "Set UMBRAL_CLIENT_ID in UmbralSettings.swift, then build again."

    var body: some View {
        NavigationStack(path: $path) {
            VStack(alignment: .leading, spacing: 16) {
                Text("Umbral sample").font(.largeTitle)
                Button("Public") { message = nil; path = [.public] }
                Button("Private") {
                    if claims != nil { message = nil; path = [.private] } else { signIn(then: [.private]) }
                }
                if claims == nil {
                    Button("Sign in") { signIn(then: []) }
                } else {
                    Button("Sign out") { signOut() }
                }
                errorText
                Spacer()
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(24)
            .navigationDestination(for: Screen.self) { screen in
                VStack(alignment: .leading, spacing: 16) {
                    switch screen {
                    case .public:
                        Text("Public").font(.largeTitle)
                        Text("This page is open to anyone, signed in or not.")
                    case .private:
                        Text("Private").font(.largeTitle)
                        if let claims {
                            ForEach(claims, id: \.name) { claim in
                                HStack(alignment: .firstTextBaseline, spacing: 16) {
                                    Text(claim.name).font(.headline).frame(width: 120, alignment: .leading)
                                    Text(claim.value)
                                }
                            }
                        } else {
                            Text("Your session ended. Go back and sign in again.")
                        }
                    }
                    errorText
                    Spacer()
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(24)
                .navigationBarTitleDisplayMode(.inline)
            }
        }
        .onAppear { claims = auth.claims() }
        .onChange(of: path) { claims = auth.claims() }
    }

    @ViewBuilder private var errorText: some View {
        if let message { Text(message).foregroundStyle(.red) }
    }

    /// Where to go once a sign-in succeeds: Home for "Sign in", Private for opening Private.
    private func signIn(then destination: [Screen]) {
        guard !UmbralSettings.UMBRAL_CLIENT_ID.trimmingCharacters(in: .whitespaces).isEmpty else {
            message = Self.setClientIdMessage
            return
        }
        message = nil
        auth.signIn { error in
            claims = auth.claims()
            message = error
            if error == nil { path = destination }
        }
    }

    private func signOut() {
        message = nil
        auth.signOut { error in message = error }
        claims = nil
        path = []
    }
}
