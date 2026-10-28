# Bank anti-corruption layer

The Application core depends on one outbound port, `IPaymentGateway`, implemented here by
`PaymentGateway`. Each concrete bank adapter is a plug-in of that gateway: it implements
`IPaymentGatewayProvider` (in `Providers/`), the adapter's own provider contract and the ACL
boundary. `IPaymentGatewayProviderSelector` (`ConfiguredProviderSelector`) is the single owner of
provider selection: it is the only reader of `PaymentGatewayOptions` and owns the provider registry.
`SelectForLinkCreation` returns the providers to try in priority order (or exactly the explicitly
named one) with the link-creation timeout; `SelectRetained` returns the provider an attempt was
created with and never consults priority. `GatewayCallExecutor` handles execution deadlines and
transport failures; it does not replace the ACL.

## High-level gateway

`PaymentGatewayService` validates business inputs and depends only on `IPaymentGateway`
(plus its logger). `PaymentGateway` implements that port here and owns provider
selection, fallback, fresh request IDs, registry lookup, call deadlines, result
correlation and integration logging. Design: `src/Modules/Payment/docs/gateway-boundary-refactor-design.md`.

Where the types live:

| Location | Contents |
|---|---|
| `Application/Domain/ValueObjects/` | Provider-neutral types: `GatewayAttempt`, link/callback results, callback-only `GatewayPaymentState`, `GatewayReversalResult`, `GatewayInquiryResult`, and `PaymentInquiryResult` |
| `Application/Ports/` | `IPaymentGatewayService` (inbound) and `IPaymentGateway` (the only outbound port) |
| `Application/Service/` | `PaymentGatewayService`; `Errors/PaymentServiceErrors` |
| `Adapter/Outbound/Gateways/` | `PaymentGateway`, `IPaymentGatewayProviderSelector`/`ConfiguredProviderSelector`, call executor, `PaymentGatewayOptions`, `PaymentGatewayErrors` |
| `Adapter/Outbound/Gateways/Providers/` | The provider plug-in contract: `IPaymentGatewayProvider`, capability interfaces and their messages |

Application defines only what its core depends on. Everything that exists to build
`PaymentGateway` lives beside it, and no port references another port's folder.

## Callback verification is the confirmation

There is no separate Confirm operation. `VerifyCallbackAsync` is the whole success path:
the provider applies its documented callback checks and, only when the callback reports
payment, confirms it with its bank server-side (Parsian `ConfirmPayment`, Mellat
`bpVerifyRequest`). A callback on its own proves nothing; `Confirmed` always means the bank
confirmed. A failed check is rejected without contacting the bank.

| Outcome | Meaning |
|---|---|
| `Confirmed` | The bank confirmed the payment. Claims nothing about settlement of funds. |
| `Failed` | Cancelled, a documented failure code, or the bank refused a known confirmation. |
| `Reversed` | The bank reports the payment already reversed. |
| `Unknown` | Transport failure after the retry, a malformed or mismatched reply, or any undocumented code. Never a final failure. |

The callback returns the attempt to retain. A confirmed attempt carries the bank reference
(`GatewayAttempt.WithConfirmation`). **Duplicate policy:** a callback for an attempt that is
already confirmed returns the stored `Confirmed` outcome with `IsDuplicate = true` and never
contacts the bank, whatever the bank would answer to a second confirmation. There is no
amount comparison: the banks' amount fields are not reliable evidence (Mellat's `FinalAmount`
legitimately differs after an online discount).

Failed checks and malformed callbacks are logged with event `PaymentCallbackSuspicious`
(4101); operational failures (timeout, provider not registered) are ordinary warnings.

Reversal and inquiry are separate capability interfaces on the same provider instance:
`IPaymentReversalProvider` and `IPaymentInquiryProvider`. They have different result types and
explicit orchestration paths; only retained-provider resolution and call execution are shared.
The gateway resolves the provider named by `IBankTransaction.GatewayAttempt`, then requires the
specific capability. A provider without it returns `PAYMENT_OPERATION_UNSUPPORTED` with no bank
call. Callback, reversal and inquiry never read policy and never fall back to another provider.

Inquiry is observational and non-mutating. It is valid for confirmed and locally unknown attempts
when the retained identifiers exist. Providers return a correlated bank amount; `PaymentGateway`
validates the observation, while `PaymentGatewayService` alone compares it with the transaction's
expected amount and returns `AmountMatchesExpected`. An amount mismatch remains a successful
evidence response. `GatewayAttempt.RequestedAt` is retained for provider report date filters.

## Validation ownership

Every validation rule has one owner — see
[ADR-0015](../../../../../../docs/adr/0015-payment-gateway.md) before
adding or changing a gateway. In short: `Money` validates the platform; each provider
request type has its own inline, stateless factory that validates what that gateway
accepts and maps to its DTO; sale factories inherit `GatewaySaleRequestFactoryBase`
for the shared completeness checks; for operations, `PaymentGatewayService` validates the
transaction and its retained attempt, `PaymentGateway` builds the context from them, and
the provider's capability method checks only that the context is its own and any
provider-specific format. Valid-but-unaccepted values return the generic
`PAYMENT_*_UNSUPPORTED` errors, never `PAYMENT_REQUEST_INVALID`.

Bank DTOs, SDKs, SOAP envelopes, authentication, callback field names, amount wire
formats, and response-code mappings belong inside the concrete outbound adapter.
Adapters translate to Payment-owned requests, outcomes and `Money` (IRR only).
For example, a bank's sale reference or RRN maps to `ProviderReference`; neither
name becomes a required field on the consumer's BankTransaction contract.

The Payment transaction lifecycle supplies an already-created `IBankTransaction`
to `IPaymentGatewayService` and must retain the `GatewayAttempt` returned in
`PaymentLinkResult`, then the one returned by `VerifyCallbackAsync` (Mellat's Reverse needs
its confirmed bank reference, and the duplicate policy needs its confirmation). The bank
adapter receives a snapshot with both BankTransaction identity and attempt identity;
it cannot create a BankTransaction or replace its amount. A new attempt identifier
during failover does not create a new business transaction.

Adding or changing a bank requires changes in its adapter, outbound registration
and provider configuration. It must not require changes in Catalog, another
consumer module, or the inbound gateway contract. Subsequent operations keep the
original provider. Consumer modules must not construct bank payloads or interpret
provider codes: Payment owns that integration.

## Runtime selection

`CreateLinkAsync(transaction, callbackUrl, providerName, ct)` has one overload. With a
provider name it selects exactly that registered provider, case-insensitively; missing or
ambiguous names and provider refusals return their original errors without falling back.
With `providerName: null` it uses the configured default/fallback order. An empty or
whitespace name is refused with `PAYMENT_PROVIDER_NAME_EMPTY`. Callback and subsequent
operations always resolve the provider stored in the transaction's `GatewayAttempt`.

## Parsian configuration

Parsian is disabled unless `Payment:Gateways:Parsian:Enabled` is true. Its installer
extension owns all of its options and HTTP configuration; it does not change shared
HTTP defaults or another client's address, headers, handlers or timeout.

Supply configuration through the host's existing configuration providers, for example:

```text
Payment__Gateways__Parsian__Enabled=true
Payment__Gateways__Parsian__LoginAccount=<secret supplied outside source control>
Payment__Gateways__Parsian__BaseUrl=https://pec.shaparak.ir/NewIPGRestServices
Payment__Gateways__Parsian__PaymentPageUrl=https://pec.shaparak.ir/NewIPG/
Payment__Gateways__Parsian__RequestTimeout=00:00:15
Payment__Gateways__Parsian__Inquiry__BaseUrl=https://pgwservices.pec.ir
Payment__Gateways__Parsian__Inquiry__ApiVersion=1.0
Payment__Gateways__Parsian__Inquiry__RequestTimeout=00:00:15
```

The URL and timeout values above are defaults. Set `Payment__Gateways__DefaultProvider=parsian`
only when policy-based initiation (`providerName: null`) should use it. Explicit provider
selection does not require a default provider. The callback URL remains a per-request value.
Production egress IP whitelisting is required by Parsian. No sandbox is documented.

Sale accepts whole IRR amounts from 1,000 through Int64.MaxValue, subject to the bank's
merchant-specific maximum. It uses the attempt RequestId as OrderId and returns a GET
handoff. Optional bank AdditionalData/Originator fields are omitted: the existing generic
request has no source for these values, and no provider-specific fields were added to it.

The existing callback port takes decoded form fields. An eventual REST driving adapter
must read `application/x-www-form-urlencoded` POST data, not deserialize JSON, and reject
duplicate fields before constructing its single-value dictionary. Parsian documents only the
case-sensitive `status` and `RRN` callback fields, so only those are read; `Token`/`OrderId`
are not documented and are ignored. There is no documented signature: the server-side
confirmation is the proof. `-138` is `Failed` (cancelled); other non-zero statuses are `Unknown`.

When `status == 0 && RRN > 0`, the callback calls `payment/ConfirmPayment` with the trusted
token. `Status == 0` with the same token and the same RRN (compared as numbers, so `000456`
equals `456`) is `Confirmed`. A known refusal (`-126`, `-1528`, …) is `Failed` with the code
kept; any other code, a malformed reply, a mismatched token/RRN, an HTTP error or a transport
failure after the retry is `Unknown`. Reverse uses `Payment/ReversalRequest`; a positive,
matching response token is required, known refusals return Result errors with raw codes and
unknown statuses an Unknown observation. Result.IsSuccess means an observation was obtained,
not that payment settled: callers must check the operation-specific result. Raw messages/codes
remain diagnostic data and must not be logged with credentials, tokens, callback bodies or card
data.

Confirmation and reversal retry network/connection failures once, using fresh messages.
Caller cancellation never retries. An exhausted timeout/transport error has an unknown
financial outcome. HTTP error responses and malformed payloads are not reinterpreted as
bank status codes. Sale has no automatic transport retry.

Parsian inquiry calls `POST /api/PGWReport/GetSaleReport` on a separate client. It sends the
retained RRN, OrderId, numeric token, and the start/end of the attempt's Tehran calendar day.
The `Data` reader accepts only a documented record object or an array of those records and
requires exact numeric correlation. `PGWStatusId` is retained as raw provider evidence; it is
not mapped to a local payment state. Report authentication is hidden behind the provider-local
`IParsianInquiryAccessTokenProvider`. No production implementation is registered yet, so Parsian
inquiry currently returns `PARSIAN_INQUIRY_TOKEN_PROVIDER_UNAVAILABLE` before HTTP. Link creation,
callback confirmation, and reversal remain usable. A future Redis- or database-backed token
implementation can be added without changing inquiry contracts.

## Mellat configuration

Mellat is disabled unless `Payment:Gateways:Mellat:Enabled` is true. Its provider owns a
separate typed HTTP client, Basic Authentication header, address, timeout and transport
handler. Enabling it does not change the Parsian client or any shared client configuration.

```text
Payment__Gateways__Mellat__Enabled=true
Payment__Gateways__Mellat__TerminalId=<terminal id>
Payment__Gateways__Mellat__UserName=<secret supplied outside source control>
Payment__Gateways__Mellat__UserPassword=<secret supplied outside source control>
Payment__Gateways__Mellat__BaseUrl=https://bpm.shaparak.ir/pgwchannel/rest
Payment__Gateways__Mellat__PaymentPageUrl=https://bpm.shaparak.ir/pgwchannel/startpay.mellat
Payment__Gateways__Mellat__RequestTimeout=00:00:15
Payment__Gateways__Mellat__Inquiry__Enabled=true
Payment__Gateways__Mellat__Inquiry__BaseUrl=https://bos.bpm.bankmellat.ir
Payment__Gateways__Mellat__Inquiry__UserName=<BOS secret supplied outside source control>
Payment__Gateways__Mellat__Inquiry__Password=<BOS secret supplied outside source control>
Payment__Gateways__Mellat__Inquiry__RequestTimeout=00:00:15
```

The URL and timeout values above are defaults. Test environments can override both URLs
with Mellat's test channel. The merchant server IP must be registered with Behpardakht.

Sale uses the existing attempt `RequestId` as Mellat `orderId`, formats the current Tehran
date and time, and keeps the same whole-IRR amount limits as Parsian. The required
`additionalData` and `payerId` fields are sent as an empty string and `0`. Mellat's optional
sale inputs remain provider-local nullable contract fields and are omitted until Payment has
a real source for them. A successful Sale returns a case-sensitive token and a POST handoff
containing `RefId`.

The callback requires exact, case-sensitive `RefId`, `ResCode`, `SaleOrderId` and
`SaleReferenceId` field names. As Mellat documents, `RefId` and `SaleOrderId` must match the
retained attempt, and a mismatched callback is rejected without calling Verify. `FinalAmount`
is not read. `17` and other documented failure codes are `Failed`; the documented uncertain
codes and any undocumented code are `Unknown`.

A paid callback (`ResCode == "0"`) calls `bpVerifyRequest` with the callback's
`SaleReferenceId`: `0`, `43` (already verified) and `45` (already settled) are `Confirmed`,
`48` is `Reversed`, documented refusals are `Failed`, and uncertain/undocumented codes, malformed
replies and transport failures after the retry are `Unknown`. Verify and Reverse
(`bpReversalRequest`) both intentionally send the retained `RequestId` as Mellat `orderId` and
`saleOrderId`; Reverse takes `saleReferenceId` from the confirmed attempt. Reverse handles
repeated reversal code `48` idempotently. `bpSettleRequest` is not called.

Mellat inquiry uses only the separate BOS API
`POST /bhrws/transactionInfo/getTransactionById`, authenticated with inquiry-specific Basic
credentials. It correlates exactly one record by `internalReferenceId`, optionally verifies the
returned terminal, and returns the positive IRR `amount`. Archive, date-range, terminal/order,
and `bpInquiryRequest` fallbacks are intentionally not used. When the nested inquiry configuration
is disabled, Mellat payment operations remain available and inquiry returns
`MELLAT_INQUIRY_NOT_CONFIGURED` before HTTP.

## Verification

Run the provider suite explicitly in addition to the Payment regression suite:

```text
dotnet test src/Modules/Payment/Tests/EwaysVNext.Modules.Payment.GatewayTests.csproj
dotnet test tests/PaymentTests/EwaysVNext.PaymentTests.csproj
```

Tests use fake HTTP handlers through real DI; no bank credentials or real bank calls.
