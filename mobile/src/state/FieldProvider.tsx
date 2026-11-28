import NetInfo from '@react-native-community/netinfo';
import * as Crypto from 'expo-crypto';
import * as Device from 'expo-device';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState, Platform } from 'react-native';
import { ApiError, createFieldApi } from '../api/client';
import type { FieldChangeKind, FieldJob } from '../api/types';
import { apiUrl } from '../config';
import { clearSession, loadSession, saveSession, type Session } from '../storage/session';
import { createStateStore } from '../storage/stateStore';
import { SyncEngine, type Snapshot } from '../sync/engine';
import type { ChangeInput } from '../sync/queue';

interface FieldContextValue {
  /** False until the phone has checked for a saved sign-in. */
  ready: boolean;
  session: Session | null;
  snapshot: Snapshot | null;
  signIn(email: string, password: string): Promise<string | null>;
  signOut(): Promise<void>;
  record(kind: FieldChangeKind, job: Pick<FieldJob, 'id' | 'number'>, input?: ChangeInput): Promise<string | null>;
  sync(): Promise<void>;
  dismiss(noticeId: string): Promise<void>;
}

const FieldContext = createContext<FieldContextValue | null>(null);

const api = createFieldApi(apiUrl);

/** What the token page on the web will list this phone as, so a lost one is easy to spot. */
const deviceName = Device.modelName ?? (Platform.OS === 'web' ? 'Web browser' : 'Phone');

/**
 * Holds the sign-in and the sync engine, and tells the engine when to sync: on start, when the
 * signal comes back, and when the app returns to the foreground. Screens read the snapshot and
 * call `record`; they never talk to the API themselves.
 */
export function FieldProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [session, setSession] = useState<Session | null>(null);
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const engine = useRef<SyncEngine | null>(null);

  useEffect(() => {
    loadSession()
      .then(setSession)
      .finally(() => setReady(true));
  }, []);

  // One engine per sign-in, loaded from what the phone kept for this technician.
  useEffect(() => {
    if (!session) return;

    const created = new SyncEngine({
      api,
      store: createStateStore(session.technicianId),
      token: session.token,
      newId: Crypto.randomUUID,
      now: () => new Date(),
      onChange: setSnapshot,
    });
    engine.current = created;
    void created.load().then(() => created.sync());

    return () => {
      created.stop();
      if (engine.current === created) engine.current = null;
    };
  }, [session]);

  // The signal coming back is the moment the queue has been waiting for. Any report of a
  // connection is worth a sync (one already running is joined, not repeated). These are only
  // hints: the engine also retries by itself, in case the phone never notices it reconnected.
  useEffect(() => {
    const unsubscribe = NetInfo.addEventListener((state) => {
      if (state.isConnected !== false) void engine.current?.sync();
    });

    // In a browser that has navigator.connection, NetInfo listens only to that, and going on-
    // or offline doesn't always fire it. The window's own event does.
    const onOnline = () => void engine.current?.sync();
    if (Platform.OS === 'web') globalThis.addEventListener?.('online', onOnline);

    return () => {
      unsubscribe();
      if (Platform.OS === 'web') globalThis.removeEventListener?.('online', onOnline);
    };
  }, []);

  // Coming back to the app after a while: there may be news from the office.
  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') void engine.current?.sync();
    });
    return () => subscription.remove();
  }, []);

  const signIn = useCallback(async (email: string, password: string) => {
    try {
      const signedIn = await api.signIn({ email: email.trim(), password, device: deviceName });
      const next: Session = {
        token: signedIn.token,
        expiresAt: signedIn.expiresAt,
        displayName: signedIn.displayName,
        technicianId: signedIn.technicianId,
      };
      await saveSession(next);
      setSession(next);
      return null;
    } catch (error) {
      if (!(error instanceof ApiError)) return 'Something went wrong. Try again.';
      if (error.kind === 'unauthorized') return "That email and password don't match. Check both and try again.";
      if (error.kind === 'forbidden') return `The field app is for technicians. ${error.message}`;
      return error.message;
    }
  }, []);

  // Waiting changes stay on the phone under this technician, and go out at their next sign-in.
  const signOut = useCallback(async () => {
    await clearSession();
    setSession(null);
  }, []);

  const value = useMemo<FieldContextValue>(
    () => ({
      ready,
      session,
      // A signed-out phone shows nothing of the last technician's day.
      snapshot: session ? snapshot : null,
      signIn,
      signOut,
      record: (kind, job, input) => engine.current?.record(kind, job, input) ?? Promise.resolve('Sign in first.'),
      sync: () => engine.current?.sync() ?? Promise.resolve(),
      dismiss: (id) => engine.current?.dismiss(id) ?? Promise.resolve(),
    }),
    [ready, session, snapshot, signIn, signOut],
  );

  return <FieldContext.Provider value={value}>{children}</FieldContext.Provider>;
}

export function useField(): FieldContextValue {
  const value = useContext(FieldContext);
  if (!value) throw new Error('useField must be used inside <FieldProvider>.');
  return value;
}
