# ADR-006: Put the API behind the web app's origin and sign in with a cookie

- **Status:** Accepted
- **Date:** 2026-11-26

## Context

Truckroll has two processes the browser cares about: the Next.js app and the ASP.NET Core API.
The usual ways to wire them:

1. **A token SPA.** The browser calls the API on its own origin with a bearer token (JWT) kept
   in JavaScript. That needs CORS rules, token refresh, and somewhere to keep the token that an
   XSS bug can't read, which in practice means a cookie anyway.
2. **A backend-for-frontend.** Next.js holds the session and calls the API server to server.
   Every endpoint then exists twice, once in C# and once as a Next.js route handler.
3. **Same origin.** Next.js rewrites `/api/*` and `/hubs/*` to the API. The browser sees one
   origin, and the API issues an ordinary session cookie.

The people using it are office staff on company machines and technicians on phones. There's no
third-party client that needs a token: the one non-browser client, Claude Desktop through MCP
(#16), gets its own personal access tokens on a separate endpoint.

## Decision

Same origin (option 3). `web/next.config.ts` rewrites `/api` and `/hubs` to `API_URL`, which
Aspire injects. The API signs people in with ASP.NET Core Identity and a cookie named
`truckroll.auth`: HttpOnly, `SameSite=Lax`, 12 hours sliding. Unauthenticated API calls get
`401`, never a redirect, and the web app decides where to send people.

**CSRF.** Every state-changing endpoint takes a JSON body. A `SameSite=Lax` cookie isn't sent on
cross-site POST, PUT or DELETE requests, and a cross-site page can't send
`Content-Type: application/json` without a CORS preflight, which the API never approves (it has
no CORS policy at all). Together those close the classic CSRF hole without antiforgery tokens.

## Consequences

- No CORS configuration exists to get wrong, and no token lives in JavaScript where XSS could
  read it.
- SignalR (#9) connects through the same rewrite, so the hub authenticates with the same cookie.
- The Next.js server is on the request path for API calls. Locally that's free. In production
  (v0.4) the ingress routes `/api` and `/hubs` straight to the API pods instead, keeping the same
  origin without the extra hop.
- `next/server` proxy (`web/src/proxy.ts`) can only check whether the cookie exists, not whether
  it's valid. That's fine as an optimistic redirect; the API is the authority on every request.
- If a native mobile app needs the API one day (the technician app, v0.3), it gets token auth on
  its own endpoints rather than changing this.

## Alternatives considered

- **Token SPA.** More moving parts (CORS, refresh, token storage) for no benefit to a first-party
  web app.
- **Backend-for-frontend in Next.js.** Doubles every endpoint, and puts business logic in two
  languages.
