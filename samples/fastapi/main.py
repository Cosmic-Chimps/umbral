import html
import ipaddress
import json
import os
import secrets
import sys
from urllib.parse import urlencode, urlparse

import uvicorn
from authlib.integrations.base_client import OAuthError
from authlib.integrations.starlette_client import OAuth
from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, RedirectResponse
from starlette.middleware.sessions import SessionMiddleware


def required(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        sys.exit(f"The environment variable {name} is required.")
    return value


issuer = required("UMBRAL_ISSUER")
client_id = required("UMBRAL_CLIENT_ID")
client_secret = required("UMBRAL_CLIENT_SECRET")  # a Web application is a confidential client
port = int(os.environ.get("PORT") or "8000")

# Plain http is allowed on a loopback address only, the one place Umbral
# accepts it (the quick start's http://localhost:8080). Everywhere else the
# issuer is https and so is the app.
def is_loopback(host: str | None) -> bool:
    if host == "localhost":
        return True
    try:
        return ipaddress.ip_address(host or "").is_loopback
    except ValueError:
        return False


issuer_url = urlparse(issuer)
plain_http_on_loopback = issuer_url.scheme == "http" and is_loopback(issuer_url.hostname)

app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)  # the three pages and nothing else
app.add_middleware(
    SessionMiddleware,
    # Random at every start, so there is no secret in the code; sessions end
    # when the process restarts. Set SESSION_SECRET to keep them across restarts.
    secret_key=os.environ.get("SESSION_SECRET") or secrets.token_urlsafe(32),
    session_cookie="umbral_sample_session",
    same_site="lax",
    # Secure always, so a proxy that terminates TLS and speaks http to the app
    # cannot leave the cookie without it; only the plain-http loopback trial goes
    # without. Starlette always adds HttpOnly (see its SessionMiddleware).
    https_only=not plain_http_on_loopback,
)

oauth = OAuth()
oauth.register(
    "umbral",
    client_id=client_id,
    client_secret=client_secret,
    # Discovery only: the endpoints are read from the issuer, never set by hand.
    server_metadata_url=f"{issuer.rstrip('/')}/.well-known/openid-configuration",
    client_kwargs={
        "scope": "openid profile email",
        # Umbral requires PKCE; Authlib sends it only when it is named. It also
        # checks state and nonce on the way back, and the sample never turns that off.
        "code_challenge_method": "S256",
    },
)

CLAIMS = ("sub", "name", "idp", "email", "email_verified")


def page(title: str, body: str, status: int = 200) -> HTMLResponse:
    return HTMLResponse(
        f'<!doctype html><html lang="en"><head><meta charset="utf-8"><title>{title}</title></head><body>{body}</body></html>',
        status_code=status,
    )


async def sign_in(request: Request, back_to: str):
    request.session["back_to"] = back_to  # only a path of this app, never a value from the query
    return await oauth.umbral.authorize_redirect(request, str(request.url_for("signin_callback")))


@app.get("/", response_class=HTMLResponse)
async def home(request: Request):
    session = (
        '<form method="post" action="/signout"><button type="submit">Sign out</button></form>'
        if request.session.get("user")
        else '<a href="/signin">Sign in</a>'
    )
    return page(
        "Umbral sample: FastAPI",
        f'<h1>Umbral sample: FastAPI</h1><p><a href="/public">Public</a> <a href="/private">Private</a></p><p>{session}</p>',
    )


@app.get("/public", response_class=HTMLResponse)
async def public():
    return page("Public", '<h1>Public</h1><p>Anyone can read this page, signed in or not.</p><p><a href="/">Back</a></p>')


@app.get("/private", response_class=HTMLResponse)
async def private(request: Request):
    user = request.session.get("user")
    if not user:
        return await sign_in(request, "/private")
    rows = "".join(
        f"<tr><th>{name}</th><td>{html.escape(value if isinstance(value, str) else json.dumps(value))}</td></tr>"
        for name in CLAIMS
        if (value := user.get(name)) is not None
    )
    return page("Private", f'<h1>Private</h1><table>{rows}</table><p><a href="/">Back</a></p>')


@app.get("/signin")
async def signin(request: Request):
    return await sign_in(request, "/")


@app.get("/signin-callback", name="signin_callback")
async def signin_callback(request: Request):
    try:
        token = await oauth.umbral.authorize_access_token(request)  # checks state, PKCE and the id_token's nonce
    except OAuthError as error:
        request.session.pop("back_to", None)
        return page(
            "Sign-in failed",
            f'<h1>Sign-in failed</h1><p>Umbral did not sign you in: {html.escape(error.error or "error")}.</p><p><a href="/">Back</a></p>',
            400,
        )
    # The id_token's claims, validated by Authlib: Umbral puts the same ones there as at userinfo.
    claims = token["userinfo"]
    back_to = request.session.pop("back_to", "/")
    request.session.clear()  # nothing of a previous sign-in survives
    # Only what the pages show, and the id_token, which sign-out sends as id_token_hint.
    request.session["user"] = {name: claims[name] for name in CLAIMS if name in claims}
    request.session["id_token"] = token["id_token"]
    return RedirectResponse(back_to, status_code=303)


@app.post("/signout")
async def signout(request: Request):
    id_token = request.session.pop("id_token", None)
    request.session.clear()
    metadata = await oauth.umbral.load_server_metadata()
    end_session = metadata.get("end_session_endpoint")
    if not id_token or not end_session:
        return RedirectResponse("/", status_code=303)
    # id_token_hint names the app, so the post-logout redirect URI registered for it is honoured.
    query = urlencode({"id_token_hint": id_token, "post_logout_redirect_uri": str(request.url_for("signout_callback"))})
    return RedirectResponse(f"{end_session}?{query}", status_code=303)


@app.get("/signout-callback", name="signout_callback")
async def signout_callback():
    return RedirectResponse("/", status_code=303)


if __name__ == "__main__":
    # Behind a reverse proxy that terminates TLS, uvicorn trusts X-Forwarded-*
    # from loopback only; FORWARDED_ALLOW_IPS names the proxy's address.
    uvicorn.run(app, host="0.0.0.0", port=port, proxy_headers=True)
