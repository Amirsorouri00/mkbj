# Payment Transaction Inquiry Design

## Goal

Add an authoritative, provider-neutral inquiry operation for one retained gateway payment. The caller supplies an existing `IBankTransaction`; the Payment module dispatches only to the provider recorded in its `GatewayAttempt` and returns the amount recorded by that bank. Mellat uses its online transaction-by-reference service, and Parsian uses `GetSaleReport` constrained by every retained transaction identifier.

Inquiry and reversal remain separate business operations. They may share retained-provider resolution, trusted context construction, call execution, cancellation, and safe logging, but they do not share request models, result models, response validation, or one generic follow-up dispatcher.

## Current State

The gateway foundation already provides:

- a provider-neutral inbound service and outbound `IPaymentGateway` port;
- persisted-attempt concepts through `GatewayAttempt`;
- retained-provider routing through `ConfiguredProviderSelector.SelectRetained`;
- capability interfaces for inquiry and reversal;
- provider-local HTTP clients, wire contracts, options, and registration;
- a generic call executor for cancellation, timeout, and transport translation.

The current inquiry placeholder is coupled to reversal in three ways:

1. both return `GatewayOperationResult` and use `GatewayPaymentState`;
2. `PaymentGatewayService` sends both through `FollowUpAsync`;
3. `PaymentGateway` sends both through `DispatchAsync<TCapability>`.

Neither provider currently implements `IPaymentInquiryProvider`. The Payment module currently exposes only `IBankTransaction`; it does not yet contain a concrete production `BankTransaction`, persistence mapping, or inquiry endpoint.

## Decisions

### Operation contracts are distinct

Reversal receives its own result model:

```csharp
public enum GatewayReversalState
{
    Reversed,
    StillConfirmed,
    Unknown
}

public sealed record GatewayReversalResult(
    GatewayReversalState State,
    string? Reference = null,
    string? ProviderCode = null,
    string? ProviderMessage = null);
```

Inquiry receives a data-oriented result model:

```csharp
public sealed record GatewayInquiryResult(
    Money PaidAmount,
    bool AmountMatchesExpected,
    string ProviderReference,
    DateTimeOffset? TransactionTime = null,
    string? ProviderCode = null,
    string? ProviderMessage = null);
```

A successful inquiry means the provider returned exactly one record that correlates with the retained attempt and contains a valid positive IRR amount. The bank-observed amount is returned even when it differs from `IBankTransaction.Amount`; `AmountMatchesExpected` reports the comparison without discarding evidence or turning the mismatch into an integration failure.

`GatewayPaymentState` remains for callback/confirmation behavior. Inquiry does not use it. Reversal migrates to `GatewayReversalState`, after which `GatewayOperationResult` is removed.

The inbound and outbound ports become:

```csharp
Task<Result<GatewayReversalResult>> ReverseAsync(
    IBankTransaction transaction,
    CancellationToken ct = default);

Task<Result<GatewayInquiryResult>> InquiryAsync(
    IBankTransaction transaction,
    CancellationToken ct = default);
```

The reversal capability returns `GatewayReversalResult`. The inquiry capability returns a provider-normalized observation that does not contain application comparison policy:

```csharp
public sealed record GatewayInquiryObservation(
    Money PaidAmount,
    string ProviderReference,
    DateTimeOffset? TransactionTime = null,
    string? ProviderCode = null,
    string? ProviderMessage = null);
```

`IPaymentGateway.InquiryAsync` validates that observation and constructs `GatewayInquiryResult`, including `AmountMatchesExpected`. Providers never receive a generic operation enum or generic follow-up request.

### Service orchestration is explicit

`PaymentGatewayService.ReverseAsync` and `PaymentGatewayService.InquiryAsync` become independent methods. Each method checks cancellation, validates the transaction and retained attempt, and calls its corresponding outbound port method.

The service may reuse pure validation functions such as `TransactionError` and `AttemptError`. It does not reuse an asynchronous `FollowUpAsync` delegate wrapper. This keeps the workflows independently extensible when inquiry later gains eligibility rules, audit behavior, or reconciliation policy.

Inquiry is allowed for both locally confirmed attempts and attempts whose confirmation outcome is `Unknown`. It does not require `GatewayAttempt.IsConfirmed`. This preserves inquiry as a reconciliation path after an uncertain callback or confirmation call. The attempt must still contain every identifier required by its retained provider; an unknown attempt without the required provider reference is rejected before an HTTP call.

### Gateway orchestration is explicit

`PaymentGateway.ReverseAsync` and `PaymentGateway.InquiryAsync` become separate pipelines. The current `DispatchAsync<TCapability>` method is removed and is not replaced by `DispatchAsync<TCapability, TResult>`.

The gateway may reuse a private mechanical resolver with a shape equivalent to:

```csharp
Result<RetainedCapability<TCapability>> ResolveRetained<TCapability>(
    IBankTransaction transaction)
    where TCapability : class;
```

The resolved value contains the trusted `GatewayPaymentContext`, the base provider name used for execution/logging, and the requested capability from that same provider instance. Resolution performs no fallback and reads no default-provider policy.

After resolution:

- reversal executes `IPaymentReversalProvider.ReverseAsync` and validates `GatewayReversalResult`;
- inquiry executes `IPaymentInquiryProvider.InquiryAsync`, validates the returned `GatewayInquiryObservation`, compares the amount to the transaction amount, and constructs the final `GatewayInquiryResult`;
- both continue using `IGatewayCallExecutor` for mechanical execution behavior.

### Attempts retain their request time

Parsian requires both `FromDate` and `ToDate`. Add `RequestedAt` to `GatewayAttempt`, supplied as a `DateTimeOffset` when the gateway creates the attempt. The timestamp is part of value equality and is preserved by `WithProviderReference` and `WithConfirmation`.

`PaymentGateway` receives the already-registered `TimeProvider` and passes `timeProvider.GetUtcNow()` to `GatewayAttempt.Create`. There is no ambient-clock call in domain code.

This branch has no production aggregate mapping for `GatewayAttempt`, so this change creates no database migration. The future concrete `BankTransaction` mapping must persist `RequestedAt` with the rest of the attempt.

## Mellat Inquiry

### API selection

Mellat v1 uses only:

```text
POST https://bos.bpm.bankmellat.ir/bhrws/transactionInfo/getTransactionById
```

The request contains the retained Mellat sale reference:

```json
{
  "referenceId": 228994481920
}
```

`GatewayAttempt.ProviderReference` already contains Mellat's `SaleReferenceId` after callback verification. `getTransactionById` is therefore the narrowest non-archive lookup and returns the required bank-side `amount`.

The following endpoints are outside v1:

- `getTransactionByIdFromArchive` is prohibited by scope;
- `getTransactionByDate` is an archive/batch reconciliation API;
- `getTransactionByTerminalIdAndOrderId` is current-day-only and is not an automatic fallback;
- `bpInquiryRequest` is not used because the supplied contract does not establish an amount-bearing response.

### Adapter structure

Mellat reporting has a different host and security boundary from the existing payment-channel client. Add:

- `MellatInquiryHttpClient`;
- `MellatInquiryRequest`;
- `MellatInquiryResponse` and a minimal transaction-record DTO;
- inquiry-specific options under `Payment:Gateways:Mellat:Inquiry`;
- a request factory that validates and parses the retained reference.

Inquiry options contain:

- `BaseUrl`, defaulting to `https://bos.bpm.bankmellat.ir`;
- `UserName` and `Password`, supplied through secret configuration;
- `RequestTimeout`.

The adapter does not assume existing PGW credentials are provisioned for BOS. Inquiry credentials are validated when inquiry is enabled. HTTP logging is disabled on the credential-bearing typed client, redirects and cookies are disabled, and secrets or full response payloads are never logged.

### Response validation

The provider accepts a Mellat inquiry only when:

1. HTTP transport succeeds;
2. `success` is `true`;
3. `responseCode` is `000`;
4. `transactionInfoList` contains exactly one record;
5. the record's `internalReferenceId` equals the requested `referenceId`;
6. the returned terminal, when present, equals the configured terminal;
7. `amount` is positive and can be represented as IRR `Money`.

The provider maps `amount` to `Money.Create(amount, Money.RialCurrency)`. It may map the documented transaction date/time to `TransactionTime`; failure to parse this optional value does not invalidate an otherwise correlated amount-bearing record.

Mellat response code `914` maps to a shared transaction-not-found error. Authentication or input response codes remain Mellat-owned errors. Empty, multiple, mismatched, or structurally malformed successful responses map to `PAYMENT_GATEWAY_RESPONSE_INVALID`.

Read-only inquiry may retry a transport failure once using a fresh request message. Caller cancellation is never retried.

## Parsian Inquiry

### API selection and request

Parsian uses:

```text
POST https://pgwservices.pec.ir/api/PGWReport/GetSaleReport
```

The request supplies every retained exact identifier plus the required time range:

```text
RRN       <- numeric GatewayAttempt.ProviderReference
OrderId   <- GatewayAttempt.RequestId
Token     <- numeric GatewayAttempt.Token
FromDate  <- start of the attempt's Tehran calendar day
ToDate    <- end of the attempt's Tehran calendar day
```

`RequestedAt` is converted from its stored offset to Tehran time through `TimeZoneInfo`. The request uses `DateTime` values with `Kind.Unspecified`, allowing `System.Text.Json` to produce the offset-free ISO-8601 shape illustrated by Parsian's .NET sample. Exact identifiers, not the one-day interval by itself, establish correlation.

### Authentication and client isolation

Add a separate `ParsianSaleReportHttpClient` and inquiry options under `Payment:Gateways:Parsian:Inquiry`:

- `BaseUrl`, defaulting to `https://pgwservices.pec.ir`;
- `ApiVersion`, defaulting to `1.0`;
- `AccessToken`, supplied only through secret configuration;
- `RequestTimeout`.

Introduce a provider-local `IParsianReportAccessTokenProvider`. The initial implementation reads the token through options on each inquiry, permitting secret rotation without recreating provider code. Automatic token acquisition or refresh is not implemented because the supplied contract does not define that protocol, token lifetime, or refresh semantics. The interface is the replacement seam when that contract is supplied.

The report client sends `Authorization: Bearer <token>` and `apiVersion: 1.0`. It does not reuse or mutate the existing Parsian payment client.

### Response parsing and validation

The supplied Parsian document identifies `Data` as `object` but does not establish whether its wire shape is one record or an array. The adapter uses a provider-local tolerant reader with these explicit rules:

- a JSON object containing the documented sale fields is treated as one record;
- a JSON array is treated as zero or more sale records;
- `null`, primitives, and undocumented wrapper objects are invalid responses;
- unknown extra fields are ignored;
- documented identifiers and amount retain their documented numeric types.

The provider accepts a report response only when:

1. HTTP transport succeeds;
2. application `Status` is `0`;
3. exactly one record matches RRN, OrderId, and Token numerically;
4. the record's `Amount` is positive and representable as IRR `Money`.

An empty data set or no matching record maps to transaction-not-found. Multiple matching records and identifier/type mismatches map to invalid response. Status `-8` maps to a Parsian input refusal and `-99` to a Parsian operational failure.

The supplied contract does not define `PGWStatusId` meanings. The adapter retains its raw value as diagnostic provider data but does not invent a normalized paid-status mapping. The use case starts from a transaction the Payment lifecycle considers paid; inquiry independently establishes the bank record and amount, not a new lifecycle transition.

Read-only report inquiry may retry a transport failure once. Authentication failures are not retried automatically.

## Error Semantics

Add the shared gateway error:

```text
PAYMENT_GATEWAY_TRANSACTION_NOT_FOUND (404)
```

This error is used only when the provider authoritatively reports no matching transaction or a successful report contains no correlated record. It is not used for timeouts, authentication failures, malformed responses, or undocumented provider states.

Other behavior remains:

- malformed or ambiguous provider data: `PAYMENT_GATEWAY_RESPONSE_INVALID`;
- timeout: `PAYMENT_GATEWAY_TIMEOUT`;
- transport failure: `PAYMENT_GATEWAY_UNAVAILABLE`;
- provider lacks inquiry capability: `PAYMENT_OPERATION_UNSUPPORTED`;
- missing/invalid retained identifiers: `PAYMENT_CONTEXT_INVALID` or a provider request refusal at the narrowest validating boundary;
- bank amount differs from expected amount: successful `GatewayInquiryResult` with `AmountMatchesExpected == false`.

Raw provider messages and status codes are diagnostic data. Logs contain provider name, internal bank-transaction ID, operation, and normalized outcome/error code, but never credentials, bearer tokens, gateway tokens, references, card data, request bodies, or response bodies.

## End-to-End Flows

### Inquiry

```text
caller
  -> PaymentGatewayService.InquiryAsync(IBankTransaction)
  -> validate transaction, retained attempt, and required identifiers
  -> IPaymentGateway.InquiryAsync
  -> build trusted GatewayPaymentContext
  -> resolve retained IPaymentInquiryProvider only
  -> execute provider inquiry
  -> validate/correlate provider observation
  -> compare bank PaidAmount to IBankTransaction.Amount
  -> return GatewayInquiryResult
```

No selection policy, default provider, fallback provider, callback field, or caller-supplied provider name participates in inquiry routing.

### Reversal

```text
caller
  -> PaymentGatewayService.ReverseAsync(IBankTransaction)
  -> validate transaction and retained attempt
  -> IPaymentGateway.ReverseAsync
  -> build trusted GatewayPaymentContext
  -> resolve retained IPaymentReversalProvider only
  -> execute and validate GatewayReversalResult
```

Inquiry changes do not alter reversal endpoints, identifiers, retry policies, or provider-specific status mappings except for translating existing normalized states into `GatewayReversalState`.

## Expected File Changes

### Application

- Add `GatewayInquiryResult.cs`.
- Add `GatewayReversalResult.cs` and `GatewayReversalState`.
- Remove `GatewayOperationResult.cs` after all callers migrate.
- Add `RequestedAt` to `GatewayAttempt`.
- Update `IPaymentGatewayService` and `IPaymentGateway` signatures.
- Replace shared follow-up dispatch in `PaymentGatewayService` with explicit methods.

### Shared outbound gateway adapter

- Update capability interfaces with operation-specific result types.
- Add the provider-normalized `GatewayInquiryObservation` contract.
- Add a small retained-capability resolver value/helper.
- Replace generic operation dispatch in `PaymentGateway` with explicit inquiry and reversal paths.
- Add transaction-not-found to `PaymentGatewayErrors`.

### Mellat adapter

- Add inquiry request/response contracts and request factory.
- Add a BOS-specific inquiry client and options.
- Implement `IPaymentInquiryProvider`.
- Extend registration and configuration validation without changing the existing payment client.

### Parsian adapter

- Add sale-report request/response contracts and tolerant `Data` reader.
- Add request factory and Tehran-day conversion.
- Add sale-report client, inquiry options, and access-token provider seam.
- Implement `IPaymentInquiryProvider`.
- Extend registration without changing the existing payment client.

### Tests and documentation

- Split existing parameterized inquiry/reversal service and dispatch tests.
- Add provider inquiry tests and registration/HTTP-isolation tests.
- Update gateway README and ADR-0015.
- No REST endpoint, persistence mapping, migration, scheduled reconciliation worker, or cross-module integration is added in this scope.

## Test Requirements

### Shared gateway and service

- inquiry and reversal expose different compile-time result types;
- validation failure prevents provider resolution and HTTP calls;
- both confirmed and unknown attempts are eligible when required identifiers are retained;
- an unknown attempt missing its required provider reference fails before an HTTP call;
- each operation resolves only the retained provider;
- neither operation falls back after failure;
- missing capability returns unsupported without a provider call;
- inquiry result validation rejects null/default/malformed values;
- bank amount and `AmountMatchesExpected` are preserved for both equal and unequal amounts;
- caller cancellation propagates;
- inquiry and reversal logs use their own normalized outcomes.

### Mellat

- exact BOS host, path, Basic header, and JSON request casing;
- retained sale reference is the only lookup key;
- `000` plus one correlated record returns amount;
- amount mismatch is returned, not refused;
- `914` maps to transaction-not-found;
- authentication/input codes retain Mellat ownership;
- empty, multiple, mismatched, zero-amount, malformed, HTTP-error, and transport cases;
- inquiry transport retry creates a fresh request;
- source/recorded requests never contain an archive endpoint.

### Parsian

- exact report host, path, Bearer header, API-version header, and JSON casing;
- request contains RRN, OrderId, Token, FromDate, and ToDate;
- Tehran-day range is deterministic through a fixed clock/time zone conversion;
- object and array `Data` shapes are accepted under the explicit tolerant-reader rules;
- exactly one correlated record returns amount;
- empty/no-match maps to not-found;
- duplicate matches, mismatches, invalid numeric fields, zero amount, malformed data, HTTP errors, and transport failures;
- statuses `0`, `-8`, and `-99` map as designed;
- existing Parsian payment-client headers and handler remain unchanged.

### Verification order

1. `dotnet test --project src/Modules/Payment/Tests/EwaysVNext.Modules.Payment.GatewayTests.csproj`
2. `dotnet test --project tests/PaymentTests/EwaysVNext.PaymentTests.csproj`
3. `dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`
4. `dotnet test EwaysVNext.slnx`
5. `git diff --check`

Every new guard is mutation-checked during implementation by temporarily reversing or removing it and observing the focused test fail before restoring the correct code.

## Non-Goals

- Mellat archive lookup or date-based reconciliation;
- automatic Mellat fallback from reference lookup to terminal/order lookup;
- automatic Parsian access-token acquisition or refresh without its contract;
- changing payment lifecycle state from an inquiry response;
- adding a concrete `BankTransaction` aggregate, database mapping, migration, or REST endpoint;
- background reconciliation scheduling;
- cross-provider inquiry fallback;
- exposing provider wire DTOs or sensitive report fields outside the provider adapter.

## Completion Criteria

The design is implemented when both retained providers support one-transaction inquiry, successful results always contain the bank-recorded IRR amount, inquiry and reversal have independent semantic contracts and validation paths, only mechanical infrastructure is shared, Mellat archive endpoints are absent, provider clients remain isolated, and all focused, architecture, and solution tests pass.
