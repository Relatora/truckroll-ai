import type { ReactNode } from 'react';
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type TextProps,
  type TextStyle,
  type ViewStyle,
} from 'react-native';
import type { JobStatus } from '../api/types';
import { statusLabel } from '../format';
import { fonts, radius, space, touch, usePalette, type Palette } from './theme';

/** The few building blocks every screen shares. Plain React Native: nothing to learn but this file. */

type Variant = 'title' | 'heading' | 'body' | 'strong' | 'muted' | 'small';

const sizes: Record<Variant, TextStyle> = {
  title: { fontFamily: fonts.display, fontSize: 30, lineHeight: 34, letterSpacing: -0.6 },
  heading: { fontFamily: fonts.displayMedium, fontSize: 19, lineHeight: 24, letterSpacing: -0.2 },
  body: { fontFamily: fonts.body, fontSize: 17, lineHeight: 24 },
  strong: { fontFamily: fonts.bodyBold, fontSize: 17, lineHeight: 24 },
  muted: { fontFamily: fonts.body, fontSize: 15, lineHeight: 21 },
  small: { fontFamily: fonts.bodyMedium, fontSize: 13, lineHeight: 18 },
};

export function Label({ variant = 'body', style, children, ...rest }: TextProps & { variant?: Variant }) {
  const palette = usePalette();
  const color = variant === 'muted' || variant === 'small' ? palette.muted : palette.ink;
  return (
    <Text style={[sizes[variant], { color }, style]} {...rest}>
      {children}
    </Text>
  );
}

export function Button({
  label,
  onPress,
  kind = 'primary',
  busy = false,
  disabled = false,
  style,
}: {
  label: string;
  onPress: () => void;
  kind?: 'primary' | 'secondary' | 'quiet';
  busy?: boolean;
  disabled?: boolean;
  style?: StyleProp<ViewStyle>;
}) {
  const palette = usePalette();
  const looks = buttonLooks(palette)[kind];
  const off = disabled || busy;

  return (
    <Pressable
      accessibilityLabel={label}
      accessibilityRole="button"
      accessibilityState={{ disabled: off, busy }}
      disabled={off}
      onPress={onPress}
      style={({ pressed }) => [styles.button, looks.box, { opacity: off ? 0.5 : pressed ? 0.82 : 1 }, style]}
    >
      {busy ? <ActivityIndicator color={looks.text.color as string} /> : <Text style={[styles.buttonText, looks.text]}>{label}</Text>}
    </Pressable>
  );
}

function buttonLooks(palette: Palette): Record<'primary' | 'secondary' | 'quiet', { box: ViewStyle; text: TextStyle }> {
  return {
    primary: { box: { backgroundColor: palette.ember }, text: { color: palette.onEmber } },
    secondary: { box: { backgroundColor: palette.card, borderWidth: 1.5, borderColor: palette.ink }, text: { color: palette.ink } },
    quiet: { box: { backgroundColor: 'transparent', paddingHorizontal: space.sm }, text: { color: palette.frost } },
  };
}

/** A job's status as a word with a tint. Never colour alone: the word is always there. */
export function StatusChip({ status }: { status: JobStatus }) {
  const palette = usePalette();
  const tint = statusTint(palette, status);
  return (
    <View style={[styles.chip, { backgroundColor: tint.soft }]}>
      <Text style={[sizes.small, { color: tint.strong }]}>{statusLabel[status]}</Text>
    </View>
  );
}

export function statusTint(palette: Palette, status: JobStatus): { strong: string; soft: string } {
  switch (status) {
    case 'InProgress':
      return { strong: palette.ember, soft: palette.emberSoft };
    case 'Completed':
      return { strong: palette.success, soft: palette.successSoft };
    case 'Cancelled':
      return { strong: palette.danger, soft: palette.field };
    default:
      return { strong: palette.frost, soft: palette.frostSoft };
  }
}

/** A titled block on the job screen. */
export function Section({ title, children, aside }: { title: string; children: ReactNode; aside?: ReactNode }) {
  const palette = usePalette();
  return (
    <View style={[styles.section, { backgroundColor: palette.card, borderColor: palette.line }]}>
      <View style={styles.sectionHead}>
        <Label variant="heading" style={styles.flex}>
          {title}
        </Label>
        {aside}
      </View>
      {children}
    </View>
  );
}

/** A message that needs reading: a change the office overruled, or a problem with the sync. */
export function Notice({ tone, children, action }: { tone: 'warning' | 'danger' | 'info' | 'good'; children: ReactNode; action?: ReactNode }) {
  const palette = usePalette();
  const tints = {
    warning: [palette.warningSoft, palette.warning],
    danger: [palette.warningSoft, palette.danger],
    info: [palette.frostSoft, palette.frost],
    good: [palette.successSoft, palette.success],
  } as const;
  const [background, edge] = tints[tone];

  return (
    <View accessibilityRole="alert" style={[styles.notice, { backgroundColor: background, borderLeftColor: edge }]}>
      <View style={styles.flex}>{children}</View>
      {action}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  button: {
    minHeight: touch,
    borderRadius: radius.md,
    paddingHorizontal: space.xl,
    alignItems: 'center',
    justifyContent: 'center',
  },
  buttonText: { fontFamily: fonts.bodyBold, fontSize: 17 },
  chip: { alignSelf: 'flex-start', borderRadius: radius.pill, paddingHorizontal: 10, paddingVertical: 3 },
  section: { borderRadius: radius.lg, borderWidth: 1, padding: space.lg, gap: space.md },
  sectionHead: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  notice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.md,
    borderRadius: radius.md,
    borderLeftWidth: 4,
    paddingVertical: space.md,
    paddingHorizontal: space.lg,
  },
});
