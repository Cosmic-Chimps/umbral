import SwiftUI

@main
struct UmbralSampleApp: App {
    @State private var auth = UmbralAuth()

    var body: some Scene {
        WindowGroup {
            ContentView(auth: auth)
                // ASWebAuthenticationSession normally catches the redirect itself. This hands it
                // to AppAuth if the system opens the app with it instead.
                .onOpenURL { auth.resume(with: $0) }
        }
    }
}
