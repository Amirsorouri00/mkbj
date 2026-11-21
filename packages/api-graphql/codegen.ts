import type { CodegenConfig } from '@graphql-codegen/cli';

const config: CodegenConfig = {
  schema: './schema.graphql',
  documents: ['./operations/**/*.graphql'],
  generates: {
    './src/gql/': {
      preset: 'client',
      presetConfig: {
        fragmentMasking: false,
      },
      config: {
        useTypeImports: true,
        scalars: {
          ID: 'string',
          Long: 'string',
          DateTime: 'string',
          UUID: 'string',
          Upload: 'File',
          Any: 'unknown',
        },
      },
    },
  },
  ignoreNoDocuments: false,
};

export default config;
