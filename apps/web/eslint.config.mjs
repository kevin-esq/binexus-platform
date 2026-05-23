import preset from '@binexus/config/eslint';

export default [
  ...preset,
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      // Next.js handles its own loose rules; relax import/no-default-export for pages.
      'import/no-default-export': 'off',
    },
  },
];
