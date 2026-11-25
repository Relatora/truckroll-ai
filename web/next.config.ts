import type { NextConfig } from "next";

/**
 * Where the ASP.NET Core API lives. Aspire injects API_URL when it starts the stack;
 * the fallback matches the API's own launch profile for running it on its own.
 */
const apiUrl = process.env.API_URL ?? "http://localhost:5180";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,

  // Standalone output is what Aspire's Next.js publisher (and the v0.4 container image) expects.
  output: "standalone",

  /**
   * The browser only ever talks to this origin. Requests to /api and /hubs are proxied to the
   * API, so the sign-in cookie is first-party and there are no CORS rules to get wrong
   * (docs/adr/006-same-origin-api.md).
   */
  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${apiUrl}/api/:path*` },
      { source: "/hubs/:path*", destination: `${apiUrl}/hubs/:path*` },
    ];
  },

  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
