# Truckroll Field

The technician app: the day's jobs, the customer and the equipment, start and complete, and
notes. It works with no signal and sends what was done when the phone is back in range. Expo
(React Native) with Expo Router; it talks only to `/api/field` (docs/adr/011).

## Run it

With the stack running (`aspire run`, or the `truckroll` preview), from this folder:

```powershell
npm install
npm run web          # Expo's dev server, in a browser on http://localhost:8081
npm run android      # an emulator or a phone with Expo Go
```

Sign in as the demo technician, `nadia@kestrel.test` with the password `truckroll-demo`.

The app calls the web app's origin, `http://localhost:3000`, which proxies `/api` to the API. Set
`EXPO_PUBLIC_API_URL` to reach it from somewhere else: `http://10.0.2.2:3000` from the Android
emulator, or the machine's LAN address from a phone on the same Wi-Fi. The browser build runs on
its own origin, so the API allows it by name (`Field:AppOrigins`, set by the AppHost).

To check the app without Metro running next to the stack, build once and serve the files:
`npx expo export --platform web`, then `npm run serve:web`.

## How offline works

| Piece | File | What it does |
| --- | --- | --- |
| The rules | `src/sync/queue.ts` | Pure functions: make a change, replay waiting changes over the server's copy, take the server's answers |
| The engine | `src/sync/engine.ts` | Saves each change before sending it, sends in order, retries with backoff, joins overlapping syncs |
| Storage | `src/storage/` | The day and the queue in AsyncStorage, per technician; the token in the secure store |
| The client | `src/api/client.ts` | `fetch`, with each failure named by what the app does about it |

The server's copy of the day is never edited on the phone. What the technician sees is that
copy with their waiting changes replayed on top, so when the office wins a conflict there is
nothing to undo: the change leaves the queue, the server's explanation becomes a notice, and the
next copy of the day shows what the office decided.

## Checks

```powershell
npm run lint
npm run typecheck
npm test             # Vitest: the queue, the engine against a fake server, the client
```

The repository's gate (`scripts/ci.ps1`) runs these and builds the web bundle.
