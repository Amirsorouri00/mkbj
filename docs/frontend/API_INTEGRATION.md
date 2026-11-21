# Admin API Integration

## Transport policy

GraphQL is used for typed catalog reads and mutations because the schema exposes complete response types. REST is used for product media upload because the verified endpoint accepts `multipart/form-data`. The OpenAPI snapshot does not currently describe most response bodies, so it is not used to infer response data.

## Generated contracts

- `packages/api-graphql/schema.graphql` is the checked-in runtime SDL snapshot.
- `packages/api-graphql/operations/catalog.graphql` contains handwritten operations.
- `packages/api-graphql/src/gql/` is generated and must not be edited.
- `packages/api-rest/openapi.json` is the checked-in OpenAPI snapshot.
- `packages/api-rest/src/schema.ts` is generated and must not be edited.

`pnpm generate:contracts` regenerates both packages. `pnpm check:contracts` fails when generated outputs drift.

GraphQL scalar mappings keep `ID`, `Long` and `UUID` as strings. Money and decimal values are not converted to JavaScript floating point by transport helpers.

## Requests and caching

`graphQlRequest` supports cancellation through `AbortSignal` and always supplies the current memory-held access token. Product queries use stable keys:

```text
['admin-products', normalizedVariables]
['admin-product', productId]
['product-editor-options']
```

Product mutations update the matching detail record and invalidate `admin-products`. Multipart upload has no typed response contract, so it checks the HTTP result and refetches product detail.

Safe queries use the shared limited retry policy. Mutations are not automatically retried.

Transport contract tests stub only the network boundary. They verify the actual generated-document request shape, opaque IDs, bearer headers, cancellation signals, GraphQL envelopes, REST multipart fields and normalized failures. No fixture path is present in the runtime application.

## Errors

GraphQL error extensions and REST error envelopes are normalized to message, code, HTTP status, retryable status and correlation reference. The UI distinguishes authentication, permission and ordinary request failures. Form submissions preserve backend messages in a focusable summary.

The server remains authoritative. Client validation covers empty fields and published size/format constraints to reduce avoidable requests; the backend still performs the definitive validation and normalization.
