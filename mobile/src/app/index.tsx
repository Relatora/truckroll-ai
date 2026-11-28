import { Link } from 'expo-router';
import { useState } from 'react';
import { Alert, FlatList, Platform, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { changes, formatDay, formatTime, statusLabel, typeLabel } from '../format';
import { useField } from '../state/FieldProvider';
import type { ViewJob } from '../sync/queue';
import { Button, Label, Notice, statusTint } from '../ui/components';
import { SyncBar } from '../ui/SyncBar';
import { fonts, radius, space, usePalette } from '../ui/theme';

/**
 * The technician's day, in order, as a route: one stop after another down a rail. The rail's
 * markers show where they are (done, on site, still to come) at a glance from arm's length.
 */
export default function Day() {
  const { snapshot, session, sync, dismiss, signOut } = useField();
  const palette = usePalette();
  const [refreshing, setRefreshing] = useState(false);

  const view = snapshot?.view;
  const notices = snapshot?.state.notices ?? [];
  const waiting = snapshot?.state.queue.length ?? 0;

  async function refresh() {
    setRefreshing(true);
    await sync();
    setRefreshing(false);
  }

  function confirmSignOut() {
    const message = waiting > 0
      ? `${changes(waiting)} haven't been sent yet. They stay on this phone and go out the next time you sign in.`
      : 'You can sign back in at any time.';
    // Alert.alert has no buttons on the web build; the browser's confirm stands in.
    if (Platform.OS === 'web') {
      if (globalThis.confirm(`Sign out of this phone? ${message}`)) void signOut();
      return;
    }
    Alert.alert('Sign out of this phone?', message, [
      { text: 'Stay signed in', style: 'cancel' },
      { text: 'Sign out', style: 'destructive', onPress: () => void signOut() },
    ]);
  }

  const header = (
    <View style={styles.header}>
      <View style={styles.titles}>
        <Label variant="title">{view ? formatDay(view.date) : 'Your day'}</Label>
        <Label variant="muted">
          {session?.displayName}
          {view ? `, ${view.jobs.length === 1 ? '1 job' : `${view.jobs.length} jobs`}` : ''}
        </Label>
      </View>
      <SyncBar />
      {notices.map((notice) => (
        <Notice
          key={notice.id}
          tone="warning"
          action={<Button kind="quiet" label="Got it" onPress={() => void dismiss(notice.id)} />}
        >
          <Label>{notice.message}</Label>
        </Notice>
      ))}
    </View>
  );

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={[styles.flex, { backgroundColor: palette.background }]}>
      <FlatList
        contentContainerStyle={styles.list}
        data={view?.jobs ?? []}
        keyExtractor={(job) => String(job.id)}
        ListHeaderComponent={header}
        ListEmptyComponent={
          <View style={styles.empty}>
            <Label variant="heading">{view ? 'Nothing booked for you today.' : 'Getting your day.'}</Label>
            <Label variant="muted">
              {view
                ? 'New jobs appear here when the office sends them. Pull down to check.'
                : 'This needs a signal once. After that the day stays on this phone.'}
            </Label>
          </View>
        }
        ListFooterComponent={<Button kind="quiet" label="Sign out of this phone" onPress={confirmSignOut} style={styles.signOut} />}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void refresh()} tintColor={palette.muted} />}
        renderItem={({ item, index }) => <Stop job={item} first={index === 0} last={index === (view?.jobs.length ?? 0) - 1} />}
      />
    </SafeAreaView>
  );
}

function Stop({ job, first, last }: { job: ViewJob; first: boolean; last: boolean }) {
  const palette = usePalette();
  const tint = statusTint(palette, job.status);
  const [clock, meridiem] = splitTime(formatTime(job.windowStart));
  const done = job.status === 'Completed';

  return (
    <Link href={{ pathname: '/job/[id]', params: { id: String(job.id) } }} asChild>
      <Pressable
        accessibilityLabel={`${job.number}, ${job.customer.name}, ${job.place.street}, ${formatTime(job.windowStart)}, ${statusLabel[job.status]}`}
        accessibilityRole="link"
        testID={`job-${job.number}`}
      >
        {/* Layout lives on an inner view: on the web, Link asChild drops a Pressable's style function. */}
        {({ pressed }) => (
          <View style={[styles.stop, { opacity: pressed ? 0.85 : 1 }]}>
            <View style={styles.when}>
              <Label style={[styles.clock, done && { color: palette.muted }]}>{clock}</Label>
              <Label variant="small">{meridiem}</Label>
            </View>

            {/* The rail: a line through the day, with a marker for each stop. */}
            <View style={styles.rail}>
              <View style={[styles.line, { backgroundColor: first ? 'transparent' : palette.line }]} />
              <View
                style={[
                  styles.marker,
                  job.status === 'Dispatched'
                    ? { borderColor: tint.strong, backgroundColor: palette.background }
                    : { borderColor: tint.strong, backgroundColor: tint.strong },
                ]}
              />
              <View style={[styles.line, styles.grow, { backgroundColor: last ? 'transparent' : palette.line }]} />
            </View>

            <View style={[styles.card, { backgroundColor: palette.card, borderColor: palette.line }]}>
              <View style={styles.cardTop}>
                <Label variant="small">
                  {job.number}, {typeLabel[job.type].toLowerCase()}
                </Label>
                {job.priority === 'Emergency' ? (
                  <Label variant="small" style={{ color: palette.danger }}>
                    Emergency
                  </Label>
                ) : null}
              </View>
              <Label variant="strong" numberOfLines={1}>
                {job.customer.name}
              </Label>
              <Label variant="muted" numberOfLines={1}>
                {job.place.street}, {job.place.city}
              </Label>
              <View style={styles.cardBottom}>
                <Label variant="small" style={{ color: tint.strong }}>
                  {statusLabel[job.status]}
                </Label>
                {job.waiting > 0 ? <Label variant="small">{changes(job.waiting)} waiting to send</Label> : null}
              </View>
            </View>
          </View>
        )}
      </Pressable>
    </Link>
  );
}

/** "2:00 p.m." into "2:00" and "p.m.", so the time can be set large and the half of day small. */
function splitTime(time: string): [string, string] {
  const space = time.indexOf(' ');
  return space < 0 ? [time, ''] : [time.slice(0, space), time.slice(space + 1)];
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  grow: { flex: 1 },
  list: { padding: space.lg, paddingBottom: space.xxl, maxWidth: 640, width: '100%', alignSelf: 'center' },
  header: { gap: space.md, marginBottom: space.xl },
  titles: { gap: space.xs, paddingTop: space.sm },
  empty: { gap: space.sm, paddingVertical: space.xl },
  stop: { flexDirection: 'row', minHeight: 132 },
  when: { width: 58, paddingTop: space.lg, alignItems: 'flex-end' },
  clock: { fontFamily: fonts.display, fontSize: 22, lineHeight: 26, letterSpacing: -0.4 },
  rail: { width: 32, alignItems: 'center' },
  line: { width: 2, height: space.lg + 6 },
  marker: { width: 16, height: 16, borderRadius: 8, borderWidth: 3 },
  card: { flex: 1, borderWidth: 1, borderRadius: radius.lg, padding: space.lg, gap: 2, marginBottom: space.md },
  cardTop: { flexDirection: 'row', justifyContent: 'space-between', gap: space.sm },
  cardBottom: { flexDirection: 'row', justifyContent: 'space-between', gap: space.sm, marginTop: space.sm },
  signOut: { alignSelf: 'center', marginTop: space.lg },
});
