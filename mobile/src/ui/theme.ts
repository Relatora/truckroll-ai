import { useColorScheme } from 'react-native';

/**
 * The web app's palette (web/src/app/globals.css), cut down to what a phone screen needs and
 * stepped up for where it's used: a basement, a roof in full sun, a van at 6 a.m. That means
 * bigger type, larger targets and stronger contrast than the office screens.
 *
 * Ember is the colour of work being done (Start, Complete, a job in progress). Frost marks what's
 * been sent to them. Everything else stays ink and paper.
 */
export interface Palette {
  background: string;
  card: string;
  ink: string;
  muted: string;
  line: string;
  field: string;
  ember: string;
  emberSoft: string;
  onEmber: string;
  frost: string;
  frostSoft: string;
  success: string;
  successSoft: string;
  warning: string;
  warningSoft: string;
  danger: string;
}

const daylight: Palette = {
  background: '#f6f7fb',
  card: '#ffffff',
  ink: '#0e1526',
  muted: '#586278',
  line: 'rgba(14, 21, 38, 0.10)',
  field: '#eef1f7',
  ember: '#d94f12', // Darker than the web's #f25f1c so white text on it passes 4.5:1.
  emberSoft: '#ffe8dc',
  onEmber: '#ffffff',
  frost: '#1f6fd6',
  frostSoft: '#e2eeff',
  success: '#12875a',
  successSoft: '#dcf4e9',
  warning: '#8a5900',
  warningSoft: '#fdf0d6',
  danger: '#c4281c',
};

const afterHours: Palette = {
  background: '#060a14',
  card: '#0e1527',
  ink: '#edf1fa',
  muted: '#9aa5bd',
  line: 'rgba(255, 255, 255, 0.10)',
  field: '#131b31',
  ember: '#ff7a3d',
  emberSoft: '#3a1a0c',
  onEmber: '#160700',
  frost: '#5ea2ff',
  frostSoft: '#0f2244',
  success: '#3ccf8e',
  successSoft: '#0b2a1e',
  warning: '#f2b544',
  warningSoft: '#2e2106',
  danger: '#ff6b5e',
};

export const fonts = {
  display: 'BricolageGrotesque_700Bold',
  displayMedium: 'BricolageGrotesque_600SemiBold',
  body: 'Figtree_400Regular',
  bodyMedium: 'Figtree_500Medium',
  bodyBold: 'Figtree_700Bold',
} as const;

/** A thumb-sized target: Android's 48dp, rounded up for gloves. */
export const touch = 52;

export const space = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

export const radius = { sm: 8, md: 12, lg: 16, pill: 999 } as const;

/** The palette for the phone's light or dark setting. */
export function usePalette(): Palette {
  return useColorScheme() === 'dark' ? afterHours : daylight;
}
