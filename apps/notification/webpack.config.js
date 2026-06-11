const { NxAppWebpackPlugin } = require('@nx/webpack/app-plugin');
const { join } = require('path');

const repoRoot = join(__dirname, '../..');
const lib = (p) => join(repoRoot, 'libs/shared', p, 'src/index.ts');

// Third-party node_modules to keep EXTERNAL (require()d at runtime from the image's
// node_modules). Everything NOT in this list — notably the @vietride/* workspace libs
// — is bundled INTO main.js. The libs are TS-source-only (package.json main →
// src/index.js is never emitted) and their node_modules symlinks point at
// libs/shared/* which the runtime image doesn't ship, so leaving them external dangles
// at runtime ("Cannot find module '@vietride/...'"). Bundling sidesteps runtime
// workspace-lib resolution. Keep this list in sync with the runtime third-party deps.
const EXTERNAL_DEPENDENCIES = [
  '@nestjs/common',
  '@nestjs/core',
  '@nestjs/platform-express',
  '@nestjs/platform-socket.io',
  '@nestjs/swagger',
  '@nestjs/websockets',
  '@nestjs/mapped-types',
  'amqplib',
  'class-transformer',
  'class-transformer/storage',
  'class-validator',
  'dotenv',
  'reflect-metadata',
  'rxjs',
  'tslib',
  'zod',
];

module.exports = {
  output: {
    path: join(__dirname, '../../dist/apps/notification'),
    clean: true,
    ...(process.env.NODE_ENV !== 'production' && {
      devtoolModuleFilenameTemplate: '[absolute-resource-path]',
    }),
  },
  resolve: {
    extensions: ['.ts', '.js'],
    // Resolve the @vietride/* imports to each lib's TS source so webpack can BUNDLE
    // them (the plugin does not apply the tsconfig `paths` when a module is bundled
    // rather than externalized).
    alias: {
      '@vietride/contracts': lib('contracts'),
      '@vietride/nest-common': lib('nest-common'),
      '@vietride/nest-config': lib('nest-config'),
      '@vietride/nest-persistence': lib('nest-persistence'),
      '@vietride/nest-rabbitmq': lib('nest-rabbitmq'),
      '@vietride/nest-redis': lib('nest-redis'),
    },
  },
  plugins: [
    new NxAppWebpackPlugin({
      target: 'node',
      compiler: 'tsc',
      main: './src/main.ts',
      tsConfig: './tsconfig.app.json',
      assets: ['./src/assets'],
      optimization: false,
      outputHashing: 'none',
      generatePackageJson: true,
      sourceMap: true,
      // Externalize ONLY the real third-party deps; bundle the @vietride/* workspace
      // libs into main.js (resolved via the alias above).
      externalDependencies: EXTERNAL_DEPENDENCIES,
    }),
  ],
};
