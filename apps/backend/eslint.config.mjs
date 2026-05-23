import preset from '@binexus/config/eslint';

export default [
  ...preset,
  {
    files: ['src/**/*.ts'],
    rules: {
      // NestJS uses decorators heavily; allow empty constructors for DI
      '@typescript-eslint/no-extraneous-class': 'off',
      '@typescript-eslint/no-empty-function': 'off',
    },
  },
];
