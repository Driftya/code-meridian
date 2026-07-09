import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['tests/**/*.test.ts'],
    exclude: ['dist/**', 'node_modules/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text-summary', 'cobertura', 'lcov'],
      reportsDirectory: 'coverage',
    },
  },
});
