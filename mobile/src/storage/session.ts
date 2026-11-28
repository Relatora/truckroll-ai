import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

/** Who this phone is signed in as. The token is the only secret the app holds. */
export interface Session {
  token: string;
  expiresAt: string;
  displayName: string;
  technicianId: number;
}

const Key = 'truckroll.session';

/**
 * The token lives in the platform's secure store: the iOS Keychain or Android's Keystore-backed
 * storage, out of reach of other apps and of backups. The web build has no such thing, so there
 * it falls back to local storage. The web build is a development aid; technicians use the phone.
 */
const secure = Platform.OS !== 'web';

export async function loadSession(): Promise<Session | null> {
  const raw = secure ? await SecureStore.getItemAsync(Key) : await AsyncStorage.getItem(Key);
  if (!raw) return null;

  const session = JSON.parse(raw) as Session;
  // An expired token would only earn a 401; skip straight to the sign-in screen.
  return new Date(session.expiresAt) > new Date() ? session : null;
}

export async function saveSession(session: Session): Promise<void> {
  const raw = JSON.stringify(session);
  if (secure) await SecureStore.setItemAsync(Key, raw);
  else await AsyncStorage.setItem(Key, raw);
}

export async function clearSession(): Promise<void> {
  if (secure) await SecureStore.deleteItemAsync(Key);
  else await AsyncStorage.removeItem(Key);
}
