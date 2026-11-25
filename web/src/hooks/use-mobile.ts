import { useSyncExternalStore } from "react"

const MOBILE_BREAKPOINT = 768
const query = `(max-width: ${MOBILE_BREAKPOINT - 1}px)`

function subscribe(onChange: () => void) {
  const mql = window.matchMedia(query)
  mql.addEventListener("change", onChange)
  return () => mql.removeEventListener("change", onChange)
}

/**
 * True below the tablet breakpoint. Reads the media query through useSyncExternalStore, so
 * the value is right on the first client render (no flash of the desktop layout) and React
 * doesn't need a setState inside an effect. The server assumes desktop.
 */
export function useIsMobile() {
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(query).matches,
    () => false
  )
}
