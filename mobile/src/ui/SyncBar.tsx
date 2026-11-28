import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { changes, formatAge } from '../format';
import { useField } from '../state/FieldProvider';
import { Label } from './components';
import { radius, space, usePalette } from './theme';

/**
 * One line that always tells the truth about the phone's copy: is everything sent, how old is
 * the day, and what happens next with anything waiting. Tapping it syncs now.
 */
export function SyncBar() {
  // The bar trusts the last sync, not the phone's idea of its connection: a phone can report a
  // network that goes nowhere (hotel Wi-Fi), or report none while a sync gets through.
  const { snapshot, sync, signOut } = useField();
  const palette = usePalette();
  const now = useMinuteClock();

  if (!snapshot) return null;
  const waiting = snapshot.state.queue.length;
  const synced = snapshot.state.lastSyncedAt;
  const age = synced ? `Updated ${formatAge(synced, now)}.` : 'Not downloaded yet.';

  let tone: 'good' | 'busy' | 'waiting' | 'stop';
  let message: string;
  if (snapshot.status === 'signed-out') {
    tone = 'stop';
    message = waiting > 0
      ? `This phone was signed out on the web. Sign in again to send ${changes(waiting)}.`
      : 'This phone was signed out on the web. Sign in again.';
  } else if (snapshot.status === 'syncing') {
    tone = 'busy';
    message = waiting > 0 ? `Sending ${changes(waiting)}.` : 'Checking for news from the office.';
  } else if (snapshot.status === 'offline') {
    tone = 'waiting';
    message = waiting > 0
      ? `No signal. ${changes(waiting)} saved on this phone, sent when you're back online.`
      : `No signal. ${age}`;
  } else if (snapshot.status === 'error') {
    tone = 'waiting';
    message = `${snapshot.problem ?? 'Truckroll had a problem.'}${waiting > 0 ? ` ${changes(waiting)} kept on this phone.` : ''}`;
  } else {
    tone = 'good';
    message = waiting > 0 ? `${changes(waiting)} waiting to send.` : `Everything is sent. ${age}`;
  }

  const colours = {
    good: [palette.successSoft, palette.success],
    busy: [palette.frostSoft, palette.frost],
    waiting: [palette.warningSoft, palette.warning],
    stop: [palette.warningSoft, palette.danger],
  } as const;
  const [background, dot] = colours[tone];

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityHint={tone === 'stop' ? 'Goes to sign-in' : 'Syncs now'}
      onPress={() => void (tone === 'stop' ? signOut() : sync())}
      style={[styles.bar, { backgroundColor: background }]}
      testID="sync-bar"
    >
      <View style={[styles.dot, { backgroundColor: dot }]} />
      <Label variant="muted" style={[styles.text, { color: palette.ink }]} accessibilityLiveRegion="polite">
        {message}
      </Label>
    </Pressable>
  );
}

/** Re-renders once a minute, so "Updated 3 minutes ago" doesn't go stale on screen. */
function useMinuteClock(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000);
    return () => clearInterval(timer);
  }, []);
  return now;
}

const styles = StyleSheet.create({
  bar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    borderRadius: radius.md,
    paddingVertical: space.md,
    paddingHorizontal: space.lg,
    minHeight: 48,
  },
  dot: { width: 10, height: 10, borderRadius: 5 },
  text: { flex: 1 },
});
