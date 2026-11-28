import { BricolageGrotesque_600SemiBold, BricolageGrotesque_700Bold } from '@expo-google-fonts/bricolage-grotesque';
import { Figtree_400Regular, Figtree_500Medium, Figtree_700Bold, useFonts } from '@expo-google-fonts/figtree';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { FieldProvider, useField } from '../state/FieldProvider';
import { usePalette } from '../ui/theme';

/**
 * The app's frame: fonts, the sign-in and sync state, and which screens a signed-in technician
 * can reach. Signed out, the only screen is sign-in; signed in, the day and its jobs.
 */
export default function RootLayout() {
  const [fontsLoaded] = useFonts({
    BricolageGrotesque_600SemiBold,
    BricolageGrotesque_700Bold,
    Figtree_400Regular,
    Figtree_500Medium,
    Figtree_700Bold,
  });

  return (
    <SafeAreaProvider>
      <FieldProvider>{fontsLoaded ? <Screens /> : null}</FieldProvider>
    </SafeAreaProvider>
  );
}

function Screens() {
  const { ready, session } = useField();
  const palette = usePalette();
  if (!ready) return null;

  const signedIn = session !== null;
  return (
    <>
      <StatusBar style="auto" />
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: palette.background } }}>
        <Stack.Protected guard={signedIn}>
          <Stack.Screen name="index" />
          <Stack.Screen name="job/[id]" />
        </Stack.Protected>
        <Stack.Protected guard={!signedIn}>
          <Stack.Screen name="sign-in" />
        </Stack.Protected>
      </Stack>
    </>
  );
}
