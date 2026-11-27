import { NextResponse, type NextRequest } from "next/server";

/** Must match AccessServiceExtensions.CookieName in the API. */
const SESSION_COOKIE = "truckroll.auth";

/** Pages anyone can open. Everything else in the app needs a session. */
const PUBLIC_PATHS = new Set(["/", "/sign-in"]);

/**
 * An optimistic check: no session cookie, no app pages. It only looks for the cookie, it doesn't
 * validate it. The API does the real check on every request and answers 401, which the client
 * also turns into a trip to /sign-in. This just saves rendering a page that would bounce.
 */
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;

  if (PUBLIC_PATHS.has(pathname) || request.cookies.has(SESSION_COOKIE)) {
    return NextResponse.next();
  }

  const signIn = new URL("/sign-in", request.url);
  signIn.searchParams.set("next", pathname + search);
  return NextResponse.redirect(signIn);
}

export const config = {
  // Skip the API, hub and MCP rewrites, Next.js internals and static files. MCP clients carry a
  // token, never the sign-in cookie, so a redirect to /sign-in would only break them.
  matcher: ["/((?!api|hubs|mcp|maplibre|_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|webp|ico)$).*)"],
};
