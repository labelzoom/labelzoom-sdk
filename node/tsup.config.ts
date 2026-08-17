import { readFileSync } from 'node:fs';
import { defineConfig } from 'tsup';

// package.json is the single source of truth for the version. The client reports it in
// its User-Agent, and a second hardcoded copy in the source drifts the moment either one
// is bumped -- which is exactly what happened before the first publish.
const { version } = JSON.parse(readFileSync('./package.json', 'utf8')) as { version: string };

export default defineConfig({
  entry: ['src/index.ts'],
  // Dual ESM/CJS: this SDK targets enterprise integrations, plenty of which are still CJS.
  format: ['esm', 'cjs'],
  dts: true,
  clean: true,
  sourcemap: true,
  target: 'node20',
  define: {
    __LABELZOOM_SDK_VERSION__: JSON.stringify(version),
  },
});
