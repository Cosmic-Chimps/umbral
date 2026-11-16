/// What a person holds from before `clear-users`, in the same running app (no
/// restart): the remembered session, the refresh token and the access token
/// are all refused, and the same provider account can sign in again as a new
/// user. The clearing is `ClearUsers.clear`, the store behind the command.
module Umbral.Tests.ClearUsersSignInTests

open System
open System.Net
open System.Threading.Tasks
open Xunit
open Umbral.Infrastructure
open Umbral.Tests.Infrastructure
open Umbral.Tests.Infrastructure.Journeys
open Umbral.Tests.Infrastructure.Flows

let private scope = "openid email offline_access"

[<Fact>]
let ``after clear-users the old session, refresh token and access token are refused, and the same person signs in again as a new user`` () =
    task {
        use! app = Fixtures.createAppWith fakeSettings ignore
        let operator = Browser app.Client
        do! claimOperator app operator
        do! seedFakeConnector app
        let! clientId, secret = registerApp operator "Grafana"

        // A sign-in, and its tokens.
        let person = Browser app.Client
        let! signedIn = signInViaFake person clientId scope "fake-user-1" "sam@example.com"
        let code = Uri.UnescapeDataString(Result.defaultWith failwith signedIn)
        let! exchanged = exchangeCode app clientId secret code
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode)
        let! tokens = json exchanged
        let refreshToken = strOf tokens "refresh_token"
        let accessToken = strOf tokens "access_token"
        let! oldId = userIdOf app "fake-user-1"

        // Before: the session gets a code, and the access token reads userinfo.
        let! before = person.Get(authorizeUrl clientId scope)
        Assert.Equal(HttpStatusCode.Redirect, before.StatusCode)
        Assert.Contains("code=", location before)
        let! info = userInfo app.Client accessToken
        Assert.Equal(HttpStatusCode.OK, info.StatusCode)

        let! cleared = ClearUsers.clear app.DbConfig app.Clock false
        Assert.Equal(1, cleared.Users)

        // The remembered session: sent to sign in, not given a code, cookie cleared.
        let! refused = person.Get(authorizeUrl clientId scope)
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode)
        Assert.StartsWith("/signin?returnUrl=", location refused)

        Assert.True(
            not (person.Cookies.ContainsKey Sessions.CookieName) || person.Cookies[Sessions.CookieName] = "",
            "the session cookie is cleared"
        )

        // The refresh token from before.
        let! refreshed = refresh app.Client clientId secret refreshToken
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode)
        let! body = json refreshed
        Assert.Equal("invalid_grant", strOf body "error")

        // The access token from before.
        let! stale = userInfo app.Client accessToken
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode)

        // The same provider account signs in again: a new user, and tokens.
        let again = Browser app.Client
        let! second = signInViaFake again clientId scope "fake-user-1" "sam@example.com"
        let secondCode = Uri.UnescapeDataString(Result.defaultWith failwith second)
        let! newId = userIdOf app "fake-user-1"
        Assert.NotEqual<string>(oldId, newId)
        let! users = scalar app "SELECT COUNT(*) FROM users" []
        Assert.Equal("1", users)

        let! exchangedAgain = exchangeCode app clientId secret secondCode
        Assert.Equal(HttpStatusCode.OK, exchangedAgain.StatusCode)
        let! newTokens = json exchangedAgain
        Assert.NotEqual<string>(accessToken, strOf newTokens "access_token")
    }
