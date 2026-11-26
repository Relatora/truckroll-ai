import { useSyncExternalStore } from "react";

const subscribe = () => () => {};

/**
 * False during server rendering and hydration, true afterwards. Anything that differs between
 * server and client (the signed-in user, the current theme) waits for it, so hydration always
 * matches and React never has to throw the server's HTML away.
 */
export function useHydrated() {
  return useSyncExternalStore(
    subscribe,
    () => true,
    () => false,
  );
}
