import { defineConfig } from 'tsup';

export default defineConfig({
  entry: ['src/index.ts'],
  // Dual ESM/CJS: this SDK targets enterprise integrations, plenty of which are still CJS.
  format: ['esm', 'cjs'],
  dts: true,
  clean: true,
  sourcemap: true,
  target: 'node20',
});
