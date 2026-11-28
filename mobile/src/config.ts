/**
 * Where Truckroll is. The web app's origin: it proxies /api to the API (docs/adr/006), so the
 * phone and a browser use the same address.
 *
 * Set EXPO_PUBLIC_API_URL when it isn't this machine: an Android emulator reaches the host at
 * http://10.0.2.2:3000, and a real phone on the same Wi-Fi at the machine's LAN address.
 */
export const apiUrl = (process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:3000').replace(/\/$/, '');
