import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useField } from '../state/FieldProvider';
import { Button, Label, Notice } from '../ui/components';
import { fonts, radius, space, touch, usePalette } from '../ui/theme';

/**
 * Sign in once per phone. The app trades the password for a device token straight away and
 * never keeps the password; the token shows up on the web's token page as this phone.
 */
export default function SignIn() {
  const { signIn } = useField();
  const palette = usePalette();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (!email.trim() || !password) {
      setError('Enter your work email and password.');
      return;
    }
    setBusy(true);
    setError(null);
    const problem = await signIn(email, password);
    // On success this screen is replaced by the day, so only a failure needs handling here.
    if (problem) {
      setError(problem);
      setBusy(false);
    }
  }

  const input = [styles.input, { backgroundColor: palette.field, color: palette.ink, borderColor: palette.line }];

  return (
    <SafeAreaView style={[styles.flex, { backgroundColor: palette.background }]}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.page} keyboardShouldPersistTaps="handled">
          <View style={styles.brand}>
            <View style={[styles.mark, { backgroundColor: palette.ember }]} />
            <Label variant="heading">Truckroll</Label>
          </View>

          <View style={styles.intro}>
            <Label variant="title">Your day, with or without a signal.</Label>
            <Label variant="muted">
              Sign in once on this phone. Jobs, notes and customer details stay on it, and anything you do offline is sent when
              you’re back in range.
            </Label>
          </View>

          <View style={styles.form}>
            <View style={styles.field}>
              <Label variant="strong" nativeID="email-label">
                Work email
              </Label>
              <TextInput
                accessibilityLabelledBy="email-label"
                aria-label="Work email"
                autoCapitalize="none"
                autoComplete="email"
                autoCorrect={false}
                inputMode="email"
                onChangeText={setEmail}
                placeholder="you@kestrel.test"
                placeholderTextColor={palette.muted}
                returnKeyType="next"
                style={input}
                value={email}
              />
            </View>

            <View style={styles.field}>
              <Label variant="strong" nativeID="password-label">
                Password
              </Label>
              <TextInput
                accessibilityLabelledBy="password-label"
                aria-label="Password"
                autoComplete="current-password"
                onChangeText={setPassword}
                onSubmitEditing={() => void submit()}
                returnKeyType="go"
                secureTextEntry
                style={input}
                value={password}
              />
            </View>

            {error ? (
              <Notice tone="danger">
                <Label>{error}</Label>
              </Notice>
            ) : null}

            <Button label="Sign in" onPress={() => void submit()} busy={busy} />
          </View>

          <Label variant="small" style={styles.demo}>
            Demo technician: nadia@kestrel.test, password truckroll-demo.
          </Label>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  page: { flexGrow: 1, padding: space.xl, gap: space.xxl, justifyContent: 'center', maxWidth: 520, width: '100%', alignSelf: 'center' },
  brand: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  mark: { width: 14, height: 14, borderRadius: 4 },
  intro: { gap: space.md },
  form: { gap: space.lg },
  field: { gap: space.sm },
  input: {
    minHeight: touch,
    borderWidth: 1,
    borderRadius: radius.md,
    paddingHorizontal: space.lg,
    fontFamily: fonts.body,
    fontSize: 17,
  },
  demo: { textAlign: 'center' },
});
