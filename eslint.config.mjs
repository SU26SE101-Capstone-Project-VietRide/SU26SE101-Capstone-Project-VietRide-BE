import nx from '@nx/eslint-plugin';

export default [
  ...nx.configs['flat/base'],
  ...nx.configs['flat/typescript'],
  ...nx.configs['flat/javascript'],
  {
    ignores: ['**/dist', '**/out-tsc'],
  },
  {
    files: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx'],
    rules: {
      '@nx/enforce-module-boundaries': [
        'error',
        {
          enforceBuildableLibDependency: true,
          allow: ['^.*/eslint(\\.base)?\\.config\\.[cm]?[jt]s$'],
          depConstraints: [
            {
              sourceTag: '*',
              onlyDependOnLibsWithTags: ['*'],
            },
          ],
        },
      ],
    },
  },
  {
    files: [
      '**/*.ts',
      '**/*.tsx',
      '**/*.cts',
      '**/*.mts',
      '**/*.js',
      '**/*.jsx',
      '**/*.cjs',
      '**/*.mjs',
    ],
    // Override or add rules here
    rules: {},
  },
  {
    files: [
      'apps/{gateway,tracking,notification,rag}/**/*.ts',
      'libs/**/*.ts'
    ],
    rules: {
      'no-console': 'error',
      'no-empty': 'error',
      'eqeqeq': ['error', 'always'],
      'no-return-await': 'error',
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/explicit-function-return-type': ['error', {
        allowExpressions: true,
        allowTypedFunctionExpressions: true
      }],
      '@typescript-eslint/no-non-null-assertion': 'warn',
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-unsafe-assignment': 'error',
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_'
      }],
      '@typescript-eslint/naming-convention': [
        'error',
        { selector: 'class', format: ['PascalCase'] },
        { selector: 'interface', format: ['PascalCase'] },
        { selector: 'enum', format: ['PascalCase'] },
        { selector: 'variable', format: ['camelCase', 'UPPER_CASE'] },
        { selector: 'memberLike', modifiers: ['private'], format: ['camelCase'], leadingUnderscore: 'allow' }
      ]
    }
  }
];
