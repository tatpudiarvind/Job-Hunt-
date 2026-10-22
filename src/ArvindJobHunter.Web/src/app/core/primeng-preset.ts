import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * PrimeNG theme aligned with the app's --ajh-* design tokens (see styles.scss),
 * so PrimeNG components sit visually alongside Angular Material.
 */
export const JobHunterPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#eef4fc',
      100: '#e6eefb',
      200: '#c9d9f5',
      300: '#9dbbec',
      400: '#5f8fdb',
      500: '#1f5fbf',
      600: '#1b55ab',
      700: '#174890',
      800: '#133b76',
      900: '#0f2f5c',
      950: '#0a2040'
    }
  },
  components: {
    skeleton: {
      root: {
        background: '#e9edf3',
        animationBackground: 'rgba(255, 255, 255, 0.55)',
        borderRadius: '8px'
      }
    }
  }
});
