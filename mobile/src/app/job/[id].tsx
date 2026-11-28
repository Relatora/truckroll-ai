import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Linking, Platform, ScrollView, StyleSheet, Switch, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { FieldChangeKind } from '../../api/types';
import { formatDate, formatTime, formatWindow, typeLabel } from '../../format';
import { useField } from '../../state/FieldProvider';
import { canComplete, canStart, type ViewJob } from '../../sync/queue';
import { Button, Label, Notice, Section, StatusChip } from '../../ui/components';
import { fonts, radius, space, touch, usePalette } from '../../ui/theme';

/**
 * One job, with everything needed at the door and in front of the unit: who, where, how to get
 * in, what's installed, what was done before, and the actions for this visit. All of it comes
 * from the phone's copy, so it works in a basement.
 */
export default function Job() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { snapshot, dismiss } = useField();
  const palette = usePalette();
  const job = snapshot?.view?.jobs.find((candidate) => String(candidate.id) === id);
  const notices = snapshot?.state.notices.filter((notice) => String(notice.jobId) === id) ?? [];

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={[styles.flex, { backgroundColor: palette.background }]}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.page} keyboardShouldPersistTaps="handled">
          <Button
            kind="quiet"
            label="Back to your day"
            onPress={() => (router.canGoBack() ? router.back() : router.replace('/'))}
            style={styles.back}
          />

          {notices.map((notice) => (
            <Notice key={notice.id} tone="warning" action={<Button kind="quiet" label="Got it" onPress={() => void dismiss(notice.id)} />}>
              <Label>{notice.message}</Label>
            </Notice>
          ))}

          {job ? (
            <JobDetail job={job} />
          ) : (
            <View style={styles.missing}>
              <Label variant="heading">This job isn’t on your day any more.</Label>
              <Label variant="muted">The office may have moved it. Nothing you wrote for it is lost: it reaches the office as a note.</Label>
            </View>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function JobDetail({ job }: { job: ViewJob }) {
  const palette = usePalette();
  const warrantyEnds = job.equipment?.warrantyExpiresOn;

  return (
    <>
      <View style={styles.head}>
        <View style={styles.headRow}>
          <Label variant="small">
            {job.number}, {typeLabel[job.type].toLowerCase()}
            {job.priority === 'Emergency' ? ', emergency' : ''}
          </Label>
          <StatusChip status={job.status} />
        </View>
        <Label variant="title">{job.summary}</Label>
        <Label variant="muted">
          {formatWindow(job.windowStart, job.windowEnd)}, about {job.estimatedMinutes} minutes
        </Label>
      </View>

      <Actions job={job} />

      <Section
        title={job.customer.name}
        aside={<Button kind="secondary" label="Call" onPress={() => void Linking.openURL(`tel:${job.customer.phone}`)} />}
      >
        <Label variant="muted" selectable>
          {job.customer.phone}
        </Label>
      </Section>

      <Section
        title={job.place.street}
        aside={
          <Button
            kind="secondary"
            label="Directions"
            onPress={() =>
              void Linking.openURL(`https://www.google.com/maps/dir/?api=1&destination=${job.place.latitude},${job.place.longitude}`)
            }
          />
        }
      >
        <Label variant="muted">{job.place.city}</Label>
        {job.place.accessNotes ? (
          <View style={[styles.access, { backgroundColor: palette.warningSoft }]}>
            <Label variant="strong">Getting in</Label>
            <Label>{job.place.accessNotes}</Label>
          </View>
        ) : null}
      </Section>

      {job.equipment ? (
        <Section title={`${job.equipment.brand} ${job.equipment.model}`}>
          <Facts
            rows={[
              ['Unit', job.equipment.kind],
              ['Serial', job.equipment.serialNumber],
              ['Installed', formatDate(job.equipment.installedOn)],
              [
                'Warranty',
                warrantyEnds
                  ? `${new Date(warrantyEnds) > new Date(job.windowStart) ? 'Until' : 'Ended'} ${formatDate(warrantyEnds)}`
                  : 'None on file',
              ],
            ]}
          />
        </Section>
      ) : null}

      <Section title="Before this visit">
        {job.history.length === 0 ? (
          <Label variant="muted">No earlier jobs at this address.</Label>
        ) : (
          job.history.map((visit) => (
            <View key={visit.number} style={[styles.entry, { borderTopColor: palette.line }]}>
              <Label variant="small">
                {formatDate(visit.date)}, {visit.number}, {typeLabel[visit.type].toLowerCase()}
                {visit.technician ? `, ${visit.technician}` : ''}
              </Label>
              <Label variant="strong">{visit.summary}</Label>
              {visit.resolution ? <Label variant="muted">{visit.resolution}</Label> : null}
            </View>
          ))
        )}
      </Section>

      <Notes job={job} />
    </>
  );
}

/** Start, and Complete with what was found and done. Both work offline. */
function Actions({ job }: { job: ViewJob }) {
  const { record } = useField();
  const palette = usePalette();
  const [completing, setCompleting] = useState(false);
  const [resolution, setResolution] = useState('');
  const [firstTimeFix, setFirstTimeFix] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<FieldChangeKind | null>(null);

  async function act(kind: FieldChangeKind) {
    setBusy(kind);
    const problem = await record(kind, job, { resolution, firstTimeFix });
    setBusy(null);
    setError(problem);
    if (!problem && kind === 'complete') setCompleting(false);
  }

  if (job.status === 'Completed') {
    return (
      <Section title="What you did">
        <Label>{job.resolution ?? 'Completed.'}</Label>
      </Section>
    );
  }

  if (!canStart(job) && !canComplete(job)) return null;

  return (
    <View style={styles.actions}>
      {canStart(job) && !completing ? <Button label="Start job" onPress={() => void act('start')} busy={busy === 'start'} /> : null}

      {completing ? (
        <Section title="Complete the job">
          <Label variant="strong" nativeID="resolution-label">
            What did you find, and what did you do?
          </Label>
          <TextInput
            accessibilityLabelledBy="resolution-label"
            aria-label="What did you find, and what did you do?"
            multiline
            onChangeText={setResolution}
            placeholder="Cracked ignitor. Replaced it and tested three heat cycles."
            placeholderTextColor={palette.muted}
            style={[styles.textArea, { backgroundColor: palette.field, color: palette.ink, borderColor: palette.line }]}
            value={resolution}
          />
          <View style={styles.toggle}>
            <Label style={styles.flex} nativeID="fix-label">
              Fixed on this visit
            </Label>
            <Switch accessibilityLabelledBy="fix-label" aria-label="Fixed on this visit" onValueChange={setFirstTimeFix} value={firstTimeFix} />
          </View>
          {error ? (
            <Notice tone="danger">
              <Label>{error}</Label>
            </Notice>
          ) : null}
          <Button label="Complete job" onPress={() => void act('complete')} busy={busy === 'complete'} />
          <Button kind="quiet" label="Not yet" onPress={() => setCompleting(false)} />
        </Section>
      ) : (
        <Button
          kind={canStart(job) ? 'secondary' : 'primary'}
          label="Complete job"
          onPress={() => {
            setError(null);
            setCompleting(true);
          }}
        />
      )}
    </View>
  );
}

function Notes({ job }: { job: ViewJob }) {
  const { record } = useField();
  const palette = usePalette();
  const [body, setBody] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function add() {
    setBusy(true);
    const problem = await record('note', job, { body });
    setBusy(false);
    setError(problem);
    if (!problem) setBody('');
  }

  return (
    <Section title="Notes">
      {job.notes.length === 0 ? <Label variant="muted">No notes yet.</Label> : null}
      {job.notes.map((note, index) => (
        <View key={`${note.createdAt}-${index}`} style={[styles.entry, { borderTopColor: palette.line }]}>
          <Label variant="small">
            {note.author}, {formatTime(note.createdAt)}
            {note.waiting ? ', waiting to send' : ''}
          </Label>
          <Label>{note.body}</Label>
        </View>
      ))}
      <TextInput
        aria-label="New note"
        multiline
        onChangeText={setBody}
        placeholder="Add a note for the office"
        placeholderTextColor={palette.muted}
        style={[styles.textArea, { backgroundColor: palette.field, color: palette.ink, borderColor: palette.line }]}
        value={body}
      />
      {error ? (
        <Notice tone="danger">
          <Label>{error}</Label>
        </Notice>
      ) : null}
      <Button kind="secondary" label="Add note" onPress={() => void add()} busy={busy} />
    </Section>
  );
}

function Facts({ rows }: { rows: [string, string][] }) {
  return (
    <View style={styles.facts}>
      {rows.map(([name, value]) => (
        <View key={name} style={styles.fact}>
          <Label variant="muted" style={styles.factName}>
            {name}
          </Label>
          <Label style={styles.flex} selectable>
            {value}
          </Label>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  page: { padding: space.lg, paddingBottom: space.xxl * 2, gap: space.lg, maxWidth: 640, width: '100%', alignSelf: 'center' },
  back: { alignSelf: 'flex-start', paddingHorizontal: 0 },
  missing: { gap: space.sm, paddingVertical: space.xl },
  head: { gap: space.sm },
  headRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: space.sm },
  actions: { gap: space.md },
  access: { borderRadius: radius.md, padding: space.md, gap: 2 },
  entry: { borderTopWidth: 1, paddingTop: space.md, gap: 2 },
  facts: { gap: space.sm },
  fact: { flexDirection: 'row', gap: space.md },
  factName: { width: 84 },
  textArea: {
    minHeight: touch * 2,
    borderWidth: 1,
    borderRadius: radius.md,
    padding: space.md,
    fontFamily: fonts.body,
    fontSize: 17,
    textAlignVertical: 'top',
  },
  toggle: { flexDirection: 'row', alignItems: 'center', minHeight: touch },
});
