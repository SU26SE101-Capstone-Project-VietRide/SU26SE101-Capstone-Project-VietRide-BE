/// <reference types="node" />
module.exports = {
  displayName: 'nest-rabbitmq',
  preset: '../../../jest.preset.js',
  testEnvironment: 'node',
  transform: {
    '^.+\\.[tj]s$': ['ts-jest', { tsconfig: '<rootDir>/tsconfig.spec.json' }]
  },
  moduleFileExtensions: ['ts', 'js', 'html'],
  coverageDirectory: '../../../coverage/libs/shared/nest-rabbitmq',
  passWithNoTests: true
};