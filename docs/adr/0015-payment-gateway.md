# ADR-0015: Payment Gateway Integration

## Status

**Superseded by [ADR-0018](0018-sales-aggregate-owns-order-lifecycle.md) — 2026-10-28.**

The Payment module this describes was deleted with the rest of the Iran-market code
([ADR-0022](0022-remove-iran-specific-modules.md)). The MVP has no payment gateway at all: website
orders are paid outside the system and confirmed by an admin, and partner orders are reported paid
by the partner. Payment status now lives on the `Order` aggregate.

Kept for history. Nothing below describes code that exists.

---

Accepted and implemented in the Payment module. This is the single maintained
guide for gateway architecture, provider policies, validation ownership, and
provider onboarding.

## Purpose and scope

Payment Gateway is part of Payment, not a separate module. It is an
anti-corruption layer around external Internet Payment Gateways (IPGs). The
module exposes provider-neutral operations while each bank adapter owns its
wire protocol, credentials, status codes, request formats, and response mapping.

The payment subject is `BankTransaction`, not `Order`. A transaction must exist
before gateway work begins. This document covers provider communication,
selection, failover, callbacks, confirmation, inquiry, reversal, and validation
boundaries. Persistence and the complete BankTransaction lifecycle are separate
Payment work.

Payment amounts are Iranian rial (`IRR`). Payment's money boundary rejects other
currencies, including toman (`IRT`); consumers map their values explicitly.

## Architecture

The application service owns business-facing validation and calls one outbound
port, `IPaymentGateway`. The concrete `PaymentGateway` lives in
`Adapter/Outbound/Gateways` and coordinates provider selection, retained-provider
dispatch, timeouts, normalized results, and logging.

```text
PaymentGatewayService
    -> IPaymentGateway
        -> PaymentGateway
            -> provider selector / retained-provider resolver
            -> provider capability
                -> bank-specific HTTP/SOAP client
```

Bank contracts never cross into the Payment application core. Provider adapters
translate bank requests and responses into Payment-owned types such as
`GatewayLink`, `GatewayCallbackResult`, and operation-specific results.

## Core policies

### Provider selection for new payments

`SelectForLinkCreation` is the only place that applies current gateway
configuration and priority. An explicit provider name selects exactly that
provider and never falls back. With no explicit name, the configured default is
followed by configured fallbacks, de-duplicated and filtered to registered
providers.

Link creation uses the configured per-provider timeout, initially 10 seconds.
Unavailable, failed, or timed-out link creation allows the next configured
provider to be tried. A timeout is a failover trigger; inquiry or proof that the
first bank did not accept the request is not required before switching.

This failover policy applies only while creating a payment link. It must not
restart or switch an already-used payment attempt whose outcome is unresolved.

### Retained-provider dispatch for existing payments

Successful link creation returns a `GatewayAttempt` containing the provider name,
request ID, token, request timestamp, and later provider reference. Callback verification, inquiry,
reversal, and other follow-up operations use that stored provider name.

They must not consult `DefaultProvider`, `FallbackProviders`, or current
priority. They must resolve exactly the adapter identified by the attempt:

```text
GatewayAttempt.ProviderName = "mellat"
    -> MellatGatewayProvider
    -> VerifyCallbackAsync / InquiryAsync / ReverseAsync
```

The registry used by `SelectRetained` is a DI name-to-adapter map, not a runtime
priority decision. If the stored provider is absent or ambiguous, the operation
fails explicitly; it must never be sent to a different provider.

### Operations remain semantically separate

Inquiry and reversal share plumbing such as retained-provider resolution,
timeouts, cancellation, logging, and capability checks, but they are different
business operations:

- Inquiry obtains correlated bank evidence, including the bank-recorded amount. It does not
  infer or mutate the local payment lifecycle.
- Reversal changes the bank-side transaction and requires reversal-specific
  identifiers and response interpretation.

Provider capability interfaces, request models, result models, validation, and orchestration
paths remain separate. `GatewayPaymentState` is callback-only; reversal uses
`GatewayReversalState`, and inquiry returns `GatewayInquiryResult`/`PaymentInquiryResult`.
Only a mechanical retained-capability resolver and call executor are shared.

### Uncertain external outcomes

Client disconnection or a request timeout does not prove that the bank did not
process a request. This is especially important for confirmation and reversal.
Once an external side effect has been dispatched, cancellation may leave an
uncertain outcome. The transaction lifecycle must use inquiry or reconciliation
to establish the result; cancellation is not a rollback.

## Contracts and responsibilities

### Application service

`PaymentGatewayService` validates business inputs, transaction ownership of the
operation, and the presence of a retained attempt. It delegates gateway work and
does not know provider selection, HTTP, SOAP, provider options, or bank status
codes. For inquiry, it compares the correlated bank amount with the transaction
amount using `Money` equality and returns both the bank record and
`AmountMatchesExpected`; a mismatch is evidence, not an integration failure.

### Payment gateway

`PaymentGateway`:

- creates provider-neutral requests and retained attempts;
- applies new-payment selection and link failover;
- restores the exact provider for existing attempts;
- builds trusted context from the transaction and retained attempt, never from
  untrusted callback fields;
- verifies normalized result state and correlation;
- returns `PAYMENT_OPERATION_UNSUPPORTED` without a bank call when a provider
  lacks a capability;
- translates timeout and transport failures through the gateway executor.
- validates inquiry amount/reference correlation without interpreting whether an
  amount mismatch is acceptable.

### Provider

A provider:

- validates provider-specific request rules;
- maps provider-neutral requests to bank DTOs;
- calls the bank client;
- validates bank-specific callback and response fields;
- maps bank status codes into normalized Payment outcomes;
- retains bank-specific codes and messages inside the adapter.

Confirmation, inquiry, and reversal are not interchangeable. Add a capability
interface only when the bank supports that operation.

## Request-validation ownership

Validation belongs at the narrowest boundary that knows the rule.

1. Shared `Money` validates platform-wide currency and value invariants; it does
   not validate a bank's amount limits.
2. Each provider validates the subset of valid Payment money that its API accepts.
3. Each provider request type has one request factory. The factory validates and
   maps together, because provider rules often depend on mapping details.
4. Shared sale-request structure may live in a base factory, but provider-specific
   limits, units, callback restrictions, and identifier formats stay in the
   provider factory.
5. The service validates business inputs; the gateway validates trusted context,
   dispatch and correlation; the provider validates its own bank contract.
6. Provider-specific error codes remain provider-owned. Reuse a shared Payment
   error only when the failure has the same meaning at the boundary.
7. Provider references are compared in the provider's own representation; do not
   normalize values in a way that changes bank semantics.

| Rule | Owner |
|---|---|
| Payment currency is IRR | Payment money boundary |
| Provider amount range or precision | Provider request factory |
| Attempt has provider, token, and request ID | `GatewayAttempt` |
| Stored attempt provider matches the selected adapter | `PaymentGateway` / provider |
| Callback has required bank fields | Provider callback method |
| Result state is defined and correlated | `PaymentGateway` |
| Bank status code means confirmed/failed/unknown | Provider adapter |

## Provider-neutral lifecycle

```text
Create link
  -> select configured provider order
  -> call provider with timeout
  -> validate redirect response
  -> return GatewayAttempt and redirect

Callback / retained operation
  -> load BankTransaction and retained GatewayAttempt
  -> resolve the stored provider only
  -> build trusted GatewayPaymentContext
  -> call the provider capability
  -> validate the operation-specific result and correlation
  -> mutate nothing for inquiry; retain callback confirmation data only on callback flow
```

An incoming callback is not authoritative merely because it contains a success
flag. Provider callback validation and server-side confirmation are required
where the bank contract requires them. Duplicate callbacks must not cause a
second business-state transition; durable duplicate handling belongs to the
transaction lifecycle.

## Mellat / Behpardakht reference

The Mellat adapter is an ACL around the PGW and BOS contracts. Payment initiation,
callback verification, and reversal use the PGW client. Amount-bearing inquiry uses
a separate BOS client and credential boundary.

Important distinctions:

- `bpVerifyRequest` verifies a payment result.
- `bpSettleRequest` settles a verified payment.
- `bpReversalRequest` requests reversal and is a state-changing operation.
- `bpVerifySettleRequest` combines bank-specific verification/settlement behavior
  and must not be assumed equivalent to every other operation.

The adapter must preserve Mellat's response-code catalogue and map only codes
whose meaning is established by the bank contract. Known business refusals may
become a failed payment; malformed replies, undocumented codes, and transport
failures remain unknown or unavailable according to provider mapping policy.
Never infer an inquiry result from a missing record alone.

Mellat inquiry uses only
`POST /bhrws/transactionInfo/getTransactionById` with the retained sale reference.
It does not use archive, date-range, terminal/order, or `bpInquiryRequest` fallback
endpoints. Success requires code `000`, exactly one correlated record, an optional
matching terminal, and a positive IRR amount. Code `914` maps to the shared
transaction-not-found error. Inquiry may run for confirmed or locally unknown
attempts and never promotes the attempt to confirmed.

Mellat-specific identifiers, credentials, endpoint addresses, browser fields,
callback fields, response codes, and provisioning details belong in the Mellat
adapter and its tests, not in shared gateway abstractions.

## Parsian report inquiry and token boundary

Parsian amount inquiry uses `POST /api/PGWReport/GetSaleReport` on its own HTTP
client. The request contains retained RRN, OrderId and numeric token plus the
start and end of the attempt's Tehran calendar day. A provider-local reader accepts
only the documented sale-record object or an array of those records. Exact numeric
correlation and a positive amount are required. Raw `PGWStatusId` is returned as
provider evidence and is not mapped to a Payment state.

Bearer-token acquisition is a black box behind the internal
`IParsianInquiryAccessTokenProvider`. The report client requests a valid token,
conditionally renews the exact rejected token after one HTTP 401, and retries
authorization once. Token storage, expiry, distributed coordination, and renewal
implementation are deliberately deferred. No production implementation is
registered in this decision; missing infrastructure fails only Parsian inquiry
before HTTP and does not disable its link, callback, or reversal operations.

## Adding a new provider

To add Stripe, another bank, or an additional IPG:

1. Create a provider folder under
   `src/Modules/Payment/Adapter/Outbound/Gateways/<Provider>/`.
2. Implement `IPaymentGatewayProvider` with a stable name such as `"stripe"`.
3. Implement only the supported capability interfaces, for example
   `IPaymentReversalProvider` or `IPaymentInquiryProvider`.
4. Add a provider-local HTTP/SOAP client, options, request DTOs, response DTOs,
   and one request factory per bank request type.
5. Validate and map provider-specific rules in the provider boundary. Do not add
   bank conditionals to `PaymentGateway`.
6. Register the provider through `TryAddEnumerable`:

   ```csharp
   services.TryAddEnumerable(
       ServiceDescriptor.Transient<IPaymentGatewayProvider, StripeGatewayProvider>());
   ```

7. Register the provider's client and options in the Payment outbound installer.
   Keep credentials in environment variables or secret storage.
8. Add the provider name to new-payment configuration only after the adapter is
   deployed and tested. Configuration changes affect new link creation, not old
   attempts.
9. Treat the provider name as persisted compatibility data. Once stored on a
   `GatewayAttempt`, do not rename it without a migration/compatibility plan.
10. Add tests for registration, link creation, callback validation, capability
    support, malformed responses, timeout/transport failures, retained-provider
    dispatch, and provider-specific status mapping.

Existing `PaymentGateway` and `PaymentGatewayService` should not need changes for
a normal provider addition. A provider that requires new business semantics is a
design change and must be reviewed separately.

## Testing and operations

Provider tests must prove both success and refusal paths. In particular, test
that a missing capability makes no bank call, an invalid callback correlation is
rejected, a timeout triggers link failover only during link creation, and a
retained attempt never falls back to the current default provider.

Deployments must keep every provider needed by in-flight attempts available, or
provide an explicit compatibility/reconciliation plan before removing or
renaming one. A missing retained provider is an operational error, not a reason
to send the transaction to another bank.

## Decision history

This document consolidates the former payment gateway scope, provider foundation,
Mellat reference, and request-validation ownership records. It preserves these
decisions:

- Payment Gateway remains inside Payment.
- Providers are outbound ACL adapters.
- New link creation may fail over by configured priority and timeout.
- Existing attempts always dispatch to their retained provider.
- Validation is owned by the layer with the required knowledge.
- Inquiry and reversal share infrastructure only; their contracts and semantics
  remain distinct.
- Inquiry is non-mutating evidence; amount matching belongs to the Application
  service.
- Mellat uses the non-archive BOS reference lookup only.
- Parsian token coordination remains behind a provider-local interface with no
  production backend in the current scope.

## References

- Payment module implementation: `src/Modules/Payment/`
- Mellat bank reference PDFs supplied with the project
- Project architecture guidance: `AGENTS.md`
