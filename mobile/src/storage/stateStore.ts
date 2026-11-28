import AsyncStorage from '@react-native-async-storage/async-storage';
import type { StateStore } from '../sync/engine';
import type { FieldState } from '../sync/queue';

/**
 * The day and the queue, kept per technician. A shared phone that changes hands keeps each
 * person's unsent work apart, and it's still there when they sign back in.
 *
 * One key, one JSON document. A technician's day is a few kilobytes, so writing it whole on
 * every change is cheap and can't leave the day and the queue out of step with each other.
 * If the app ever keeps weeks of history, SQLite is the upgrade (docs/adr/011).
 */
export function createStateStore(technicianId: number): StateStore {
  const key = `truckroll.field.${technicianId}`;

  return {
    async load() {
      const raw = await AsyncStorage.getItem(key);
      return raw ? (JSON.parse(raw) as FieldState) : null;
    },
    async save(state) {
      await AsyncStorage.setItem(key, JSON.stringify(state));
    },
  };
}

/** How many changes a technician's queue holds, read without starting an engine. */
export async function waitingChanges(technicianId: number): Promise<number> {
  const state = await createStateStore(technicianId).load();
  return state?.queue.length ?? 0;
}
