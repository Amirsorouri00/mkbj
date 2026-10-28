# Adding the **Customer** & **Payment** Modules — Implementation Guide

> **Audience:** Developers extending EwaysVNext v3 (.NET 10, Hexagonal-First Modular Monolith).
>
> **How to read this.** Customer and Payment already exist in the repository. This guide walks
> through how they were built so you can build the *next* pair the same way. Every code sample
> below is **extracted from the current source**, with a `src/…:line-range` citation above it.
> If a sample and the file disagree, the file is right — please fix the guide.

## Golden rules (non-negotiable)

1. **No events, no message bus, no Wolverine, no MediatR.** Cross-module communication is a
   **direct method call** through hexagonal ports — and it is **not transactional**. Each module
   owns its own DbContext and connection, so a cross-module operation cannot roll back
   atomically. Wrap multi-step work in a `CrossModuleSaga` with a compensation per step
   (ADR-0005).
2. **An `Application/` hexagon never references another module's `Application/`.** Only the
   consumer's `Adapter/Outbound` may, and only to call the provider's inbound port.
3. **Shared data crosses boundaries exclusively as DTOs from `BuildingBlocks/Contracts/`** —
   never as domain entities.
4. **PCI-DSS stance:** raw card numbers never enter this monolith. Cards are tokenized by an
   upstream vault; we store and pass **tokens only**.
5. **Customer-facing inbound ports take an `Actor`.** It is built by the inbound adapter from
   `ClaimsPrincipal` and is **never** deserialized from a request body. Admin-only ports take no
   `Actor` — that absence is the signal that the transport already authorized.
6. **Aggregates never read the clock.** Time enters as a `DateTimeOffset now` parameter. A
   build-enforced source scan rejects `DateTimeOffset.UtcNow` inside any `Application/Domain`.
7. **Register the DbContext with `AddModuleDbContext<T>(...)`**, never a hand-rolled
   `AddDbContext`.

Rules 5–7 are not stylistic. Each one exists because its absence produced a real defect in this
codebase — see §2.

---

## 1. The business story & scenario

### Why two bounded contexts?

Our e-commerce platform has two subdomains that evolve at different speeds and for different
reasons:

- **Customer** owns the *identity-of-the-buyer* language: profiles, shipping/billing addresses,
  account standing, and the customer's *wallet* of saved payment instruments (as opaque vault
  tokens). Its ubiquitous language is "who is this customer and what do they prefer?"
- **Payment** owns the *money-movement* language: authorization, capture, refunds, failure
  codes, transaction lifecycle. Its ubiquitous language is "did the money move?"

They share *nothing behaviorally*. A payment does not care how addresses work; a customer does
not care about capture semantics. Merging them would couple compliance-sensitive money logic to
CRM-style profile logic and force both to deploy together forever. Two contexts, one transaction
boundary each, joined by a narrow synchronous contract — classic modular monolith.

### The use case

> A logged-in customer pays for order `#8842` using their saved Visa card.
>
> 1. **Payment** receives the request, carrying the caller's `Actor`.
> 2. Before doing anything it asks Customer: *"who owns this profile, and is the account in good
>    standing?"* — one ACL call answers both. A non-owner is refused before learning anything
>    about the account.
> 3. It then asks Customer: *"give me the saved payment method"* — and receives only a **token**,
>    never a PAN.
> 4. Payment creates a `PaymentTransaction` aggregate snapshotting the token, and persists.
>
> Steps 2–3 happen inside the same HTTP request as step 4, and they are **reads**, so if Customer
> says "suspended" nothing has been written and there is nothing to undo. No outbox, no events —
> direct synchronous method calls.
>
> **They are NOT one transaction.** Customer and Payment have separate DbContexts and
> connections. This particular flow is safe because the cross-module steps are read-only and the
> only *write* is Payment's own single `SaveChangesAsync` — which is the pattern to copy:
> **order the steps so reads that can refuse come before any write.** A flow whose cross-module
> steps mutate state needs a `CrossModuleSaga` with compensations instead, because a later
> failure will not undo an earlier module's write.

---

## 2. Lessons from the codebase's hardening

An independent architectural review of this template found the *structural* architecture sound —
consumer-owned ACL ports, machine-enforced module boundaries, per-module DbContexts — and the
domain, persistence-mapping and consistency layers underneath it unsound. Every finding sat below
the boundary layer.

**A template's defects get copied by everything built from it.** The nine rules below each
replaced a real defect. Read them before Step 1; each links to the step that demonstrates it.

### 2.1 Ownership is checked in the service, not assumed

**Was:** inbound ports took the subject id as a plain parameter —
`InitiateWithSavedCardAsync(Guid customerId, …)`. Any authenticated user could charge another
customer's stored card or read another user's order.

**Now:** customer-facing ports take an `Actor` (§6.2, §7.2). Writes authorize **before** mutating;
reads may authorize in the adapter against the owner id the port returned. Admin-only ports take
no `Actor` deliberately.

**Enforced by:** a route-inventory test walks the live `EndpointDataSource` and asserts every
endpoint carries `IAuthorizeData`, against a six-entry allowlist (the three health probes,
register, login,
refresh).

### 2.2 Time is a parameter, never ambient

**Was:** 15 sites across seven aggregates called `DateTimeOffset.UtcNow` directly. Tests worked
around it by faking elapsed time with zero-length TTLs, and by rewriting mapped properties
through `db.Entry(...).Property(...)` to get deterministic ordering.

**Now:** application services take `IClock`, read it **once per operation**, and pass the instant
in (§6.1, §7.1).

**Enforced by:** `DeterministicTimeTests` scans every `Application/Domain` folder and fails the
build on `DateTimeOffset.UtcNow`, `DateTime.UtcNow` or `DateTime.Now`.

> ⚠️ **Do not inject `IClock` into an aggregate.** Aggregates are built by static factories and
> rehydrated by EF; a service dependency means either constructor injection that EF
> materialization fights, or a static ambient clock — the original problem wearing a different
> hat. A parameter is honest and keeps the domain free of services.

### 2.3 A value object in a predicate is not a value object in a projection

**Was:** `Where(u => u.Email.Value == name)` on a `HasConversion` property. It is untranslatable
on SQL Server — but EF InMemory evaluates it client-side, so 11 sites including **login** passed
every test and failed against a real database.

**Now:** predicates compare the **whole** property to a value object; projections may reach
through it (§8.1).

**Enforced by:** `PersistenceContractTests`, which runs every query on both providers (§10.2).

### 2.4 Register the DbContext through the shared helper

**Was:** Brand and Supplier copied a hand-rolled `AddDbContext` block instead of calling
`AddModuleDbContext<T>`. When `EnableRetryOnFailure` was added to the shared helper, both modules
silently missed it.

**Now:** every module calls `AddModuleDbContext<T>` (§6.4).

**Enforced by:** `TransientFaultResilienceTests` enumerates all seven contexts by name.

### 2.5 A `Result`-returning method must not throw for a refusal it can express

**Was:** two currency guards threw `InvalidOperationException`, so a healthy server answered a
well-formed request with a 500 — misinforming the caller and polluting the 5xx signal used to
detect real outages.

**Now:** return the `Error`. Server *faults* are declared 500 explicitly at their production site
(§7.5); the status heuristic stays conservative.

### 2.6 Idempotency must reach the column that enforces it

**Was:** payment initiation validated `clientRequestId` and then **dropped it**, so it never
reached the filtered unique index meant to enforce it. A retried request double-charged.

**Now:** the key is threaded into the aggregate and the index is filtered so many rows may carry
NULL (§7.4).

### 2.7 The read side must project, not hydrate

**Was:** an admin query projected an **unmapped computed property**, so EF abandoned the flat
projection and materialised the whole aggregate — including an owned collection via a LEFT JOIN,
tracked — to render one row.

**Now:** project mapped columns only (§8.2).

**Enforced by:** `ReadSideProjectionTests` asserts the change tracker is **empty** after a read.

### 2.8 Paging must be bounded at the boundary

**Was:** `page` and `pageSize` travelled from the request into `.Skip((page - 1) * pageSize)`
unchecked. `page: 0` produced `OFFSET -20 ROWS`, so a client mistake surfaced as a server error;
`pageSize: 1000000` was an unbounded scan available to any authenticated admin.

**Now:** validate and **reject** — do not silently clamp (§8.3).

### 2.9 Money carries its currency, and refuses what it cannot represent

**Was:** `Order.TotalAmount` was a bare `decimal` and the currency lived in a pinned constant,
because the cross-module snapshot dropped it. The payment gate compared amounts alone, so a
capture in the wrong currency satisfied it.

**Now:** `Money` everywhere money crosses a boundary, currency **derived** from the provider
(§7.1). `Money` rounds to two decimal places and therefore **refuses** currencies that do not use
two — `MONEY_UNSUPPORTED_CURRENCY_DECIMALS` for JPY, KRW, KWD, BHD and the rest.

---

## 3. Architecture & dependency mapping

### Roles

| Module | Role | Owns |
|---|---|---|
| **Customer** | **Provider** | Profiles, addresses, saved payment methods (tokens). Exposes `ICustomerInboundPort`. |
| **Payment** | **Consumer** | Transactions, method snapshots, status lifecycle. Defines `ICustomerGatewayPort` (outbound port). |

### The synchronous call chain

```
Payment.Application.Service.PaymentService          (hexagon, orchestrates the use case)
        │ depends on (port owned by the CONSUMER)
        ▼
Payment.Application.Ports.Outbound.ICustomerGatewayPort     ◄── interface, lives in CONSUMER
        ▲ implemented by
        │
Payment.Adapter.Outbound.Gateways.CustomerGateway           ◄── internal sealed ACL gateway
        │
        │  direct method call — same process, SEPARATE transactions.
        │  Not atomic: compensated by CrossModuleSaga (ADR-0005).
        │  Reads are never stale; there is no event bus and no outbox.
        │
        ▼
Customer.Application.Ports.Inbound.ICustomerInboundPort     ◄── interface, lives in PROVIDER
        ▲ implemented by
        │
Customer.Application.Service.CustomerService                ◄── sealed service (provider hexagon)
```

> **Why the inbound port lives in `Application/`, not `Adapter/Inbound/`.** Consumers must depend
> only on the provider's **Application** assembly, never its Adapter assemblies. Prose sometimes
> says "Customer.Adapter.Inbound"; physically the port is in
> `Customer/Application/Ports/Inbound/`. This is exactly how `Orders → Catalog` works
> (`CatalogGateway` → `IProductService`).

### The sanctioned cross-module exceptions — all five

Rule 2 forbids cross-module Application references. These are the only exemptions, and each is
machine-checked (§10.1):

| Exception | Direction | Reason |
|---|---|---|
| `Orders.Adapter.Outbound` → `Catalog.Application` | Orders → Catalog | Stock reservation ACL |
| `Payment.Adapter.Outbound` → `Customer.Application` | Payment → Customer | **This guide:** customer verification ACL |
| `Supplier.Adapter.Outbound` → `Brand.Application` | Supplier → Brand | Brand existence check before agreement mutations (ADR-0009) |
| `Orders.Adapter.Outbound` → `Payment.Application` | Orders → Payment | Order-owned payment gate (ADR-0005 amendment). **Payment references Orders nowhere** |
| `Admin.Adapter.Outbound` → provider `Application` / `QueryApplication` | Admin → all | Composition layer, read + write gateways (ADR-0006/0007) |

The fourth row is worth pausing on: it is deliberately **one-directional**. The same architecture
theory that permits `Orders → Payment` simultaneously proves Payment never references Orders.

### Project reference map

```
Customer.Application            ──► DDD, SharedKernel, Contracts, Framework        (NO module refs)
Customer.Adapter.Outbound       ──► Customer.Application, Infrastructure.Persistence
Customer.Adapter.Inbound.Rest   ──► Customer.Application, Framework, Contracts
Customer.QueryApplication       ──► Customer.Adapter.Outbound (DbContext), Contracts, Framework
Customer.Installer              ──► all four above

Payment.Application             ──► DDD, SharedKernel, Contracts, Framework        (NO module refs!)
Payment.Adapter.Outbound        ──► Payment.Application, Infrastructure.Persistence,
                                    Customer.Application   ← THE EXCEPTION
Payment.Adapter.Inbound.Rest    ──► Payment.Application, Framework, Contracts
Payment.QueryApplication        ──► Payment.Adapter.Outbound, Contracts, Framework
Payment.Installer               ──► all four above
```

`Framework` is on every hexagon because that is where `Actor` and `IClock` live — a module cannot
enforce ownership or take a timestamp without it.

---

## 4. Step 0 — scaffold the ten projects

Five projects per module. The split is not ceremony: it is what makes rule 2 checkable by a
machine, because "Application must not reference Adapter" is only meaningful if they are separate
assemblies.

```
src/Modules/Customer/                                ◄── PROVIDER
├── Application/                                     ← the hexagon (no EF, no adapters)
│   ├── EwaysVNext.Modules.Customer.Application.csproj
│   ├── DependencyInjection.cs                       AddCustomerApplication()
│   ├── Domain/
│   │   ├── Customers/
│   │   │   ├── Customer.cs                          AggregateRoot (profile, wallet)
│   │   │   ├── SavedPaymentMethod.cs                Entity (token only)
│   │   │   └── Events/CustomerEvents.cs             records only — NO dispatcher
│   │   └── ValueObjects/
│   │       └── Address.cs                           Address VO + CustomerStatus smart enum
│   ├── Ports/
│   │   ├── Inbound/ICustomerInboundPort.cs          ★ the boundary Payment calls
│   │   └── Outbound/ICustomerRepository.cs          + IUnitOfWork
│   └── Service/CustomerService.cs                   sealed
├── Adapter/
│   ├── Outbound/                                    EF Core: DbContext, configs, repos, migrations
│   └── Inbound/Rest/                                minimal-API endpoints
├── QueryApplication/CustomerQueryService.cs         read side (projections only)
└── Installer/ModuleInstaller.cs                     AddCustomerModule()

src/Modules/Payment/                                 ◄── CONSUMER — same five-project shape, plus:
└── Adapter/Outbound/Gateways/CustomerGateway.cs     ★ the ONLY bridge to Customer
```

> **`CustomerStatus` lives in `Address.cs`.** Small value objects for one module are co-located.
> Do not go looking for `ValueObjects/CustomerStatus.cs`.

The consumer's outbound csproj is the single file that declares the exception:

<!-- src/Modules/Payment/Adapter/Outbound/EwaysVNext.Modules.Payment.Adapter.Outbound.csproj -->
```xml
<!-- Payment.Adapter.Outbound.csproj — the ONLY place Customer.Application may be referenced -->
<ItemGroup>
  <ProjectReference Include="..\..\..\Customer\Application\EwaysVNext.Modules.Customer.Application.csproj" />
</ItemGroup>
```

*If this reference appears in any other Payment csproj, `Only_The_Sanctioned_Gateway_Adapter_May_Reference_Provider_Application` fails the build.*

---

## 5. Step 1 — shared contracts

**WHERE:** `src/BuildingBlocks/Contracts/{Customer,Payment}/`
**RULES:** records only, no behaviour, no domain types, no EF attributes. These are the *only*
Customer vocabulary Payment will ever see.

<!-- src/BuildingBlocks/Contracts/Customer/CustomerDtos.cs:1-28 -->
```csharp
namespace EwaysVNext.Contracts.Customer;

/// <summary>
/// Cross-module view of account standing. Consumed by the Payment module via its
/// SYNCHRONOUS ACL gateway — NOT an integration event, and NOT transactional: the two
/// modules own separate DbContexts, so the call cannot participate in one transaction.
///
/// <para><c>IdentityUserId</c> is the OWNER of the profile. Payment needs it to answer
/// "does this customer profile belong to the caller?": a payment is addressed by
/// Customer aggregate id, while an <c>Actor</c> carries the identity user id, and
/// Payment cannot resolve that mapping on its own. Carrying the owner on the standing
/// DTO lets the existing verify-customer-standing ACL step answer both questions in
/// one call instead of adding a second gateway round trip.</para>
/// </summary>
public sealed record CustomerStatusDto(Guid CustomerId, Guid IdentityUserId, bool IsSuspended, string AccountTier);

/// <summary>
/// Saved payment instrument as an opaque VAULT TOKEN. Raw card numbers never
/// cross any module boundary (PCI: tokenization happens in the upstream vault).
/// </summary>
public sealed record SavedPaymentMethodDto(
    Guid PaymentMethodId,
    string TokenizedCardNumber,
    DateOnly ExpiryDate,
    string CardType);

/// <summary>Read-side projection (QueryApplication only).</summary>
public sealed record CustomerSnapshot(Guid Id, Guid IdentityUserId, string DisplayName, string Status, string AccountTier);
```

*Verbatim from `src/BuildingBlocks/Contracts/Customer/CustomerDtos.cs:1-28`.*

**`IdentityUserId` on the standing DTO is the single most important design decision in this
file.** Payment is addressed by *Customer aggregate id*; an `Actor` carries an *identity user
id*. Only Customer knows the mapping. Putting the owner on a DTO the ACL already fetches lets one
call answer both "is this account healthy?" and "does the caller own it?" — the alternative is a
second round trip on every payment.

The Payment contracts carry the vocabulary Orders needs to reason about payment state:

<!-- src/BuildingBlocks/Contracts/Payment/PaymentDtos.cs:1-41 -->
```csharp
namespace EwaysVNext.Contracts.Payment;

/// <summary>
/// Whether an order has been paid, as seen from OUTSIDE the Payment module.
///
/// <para>A typed state rather than the raw status string: Orders has to distinguish
/// "never paid" from "not captured yet" from "failed" in order to explain a refused
/// confirmation, and matching on strings across a module boundary is how status
/// vocabularies drift apart. This is shared vocabulary, which is exactly what Contracts
/// is for.</para>
/// </summary>
public enum OrderPaymentState
{
    /// <summary>No payment has ever been initiated for the order.</summary>
    None = 0,
    Pending,
    Captured,
    Failed,
    Refunded,
}

/// <summary>
/// Payment standing for ONE order — the latest transaction against it.
/// <see cref="Amount"/>/<see cref="Currency"/> are what was actually charged, so the
/// consumer can check them against its own authoritative total.
/// </summary>
public sealed record OrderPaymentStatusDto(
    Guid OrderId,
    OrderPaymentState State,
    Guid? PaymentId,
    decimal Amount,
    string Currency);

/// <summary>Read-side projection (QueryApplication only). MethodToken is a vault token, never a PAN.</summary>
public sealed record PaymentSnapshot(
    Guid Id,
    Guid CustomerId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string MethodToken);
```

*Verbatim from `src/BuildingBlocks/Contracts/Payment/PaymentDtos.cs:1-41`.*

> **Why `OrderPaymentState` is an enum and not a string.** Orders must distinguish "never paid"
> from "not captured yet" from "failed" to explain *why* it refused a confirmation. Matching on
> strings across a module boundary is how two status vocabularies silently drift apart.

---

## 6. Step 2 — provider module: **Customer**

### 6.1 Domain

<!-- src/Modules/Customer/Application/Domain/Customers/Customer.cs:1-66 -->
```csharp
using EwaysVNext.DDD.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Events;
using EwaysVNext.Modules.Customer.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class Customer : AggregateRoot<Guid>
{
    public Guid IdentityUserId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public CustomerStatus Status { get; private set; } = null!;
    public string AccountTier { get; private set; } = "Standard";
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<Address> _addresses = [];
    public IReadOnlyCollection<Address> Addresses => _addresses.AsReadOnly();

    private readonly List<SavedPaymentMethod> _paymentMethods = [];
    public IReadOnlyCollection<SavedPaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    private Customer() { }

    public static Customer Register(Guid identityUserId, string displayName, DateTimeOffset now)
    {
        if (identityUserId == Guid.Empty)
            throw new DomainException("CUSTOMER_IDENTITY_REQUIRED", "Identity user id is required.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("CUSTOMER_NAME_REQUIRED", "Display name is required.");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            IdentityUserId = identityUserId,
            DisplayName = displayName.Trim(),
            Status = CustomerStatus.Active,
            CreatedAt = now,
        };
        customer.Raise(new CustomerRegisteredDomainEvent(customer.Id, customer.IdentityUserId));
        return customer;
    }

    public void AddAddress(Address address) => _addresses.Add(address);

    /// <summary>Stores a VAULT TOKEN — this method must never receive a raw PAN.</summary>
    public SavedPaymentMethod AddSavedPaymentMethod(string tokenizedCard, string cardType, DateOnly expiry)
    {
        var method = SavedPaymentMethod.Create(Id, tokenizedCard, cardType, expiry, index: _paymentMethods.Count);
        _paymentMethods.Add(method);
        Raise(new SavedPaymentMethodAddedDomainEvent(Id, method.Id));
        return method;
    }

    public void Suspend()
    {
        if (Status == CustomerStatus.Suspended) return;
        Status = CustomerStatus.Suspended;
        Raise(new CustomerSuspendedDomainEvent(Id));
    }

    public void Reactivate()
    {
        if (Status == CustomerStatus.Active) return;
        Status = CustomerStatus.Active;
        Raise(new CustomerReactivatedDomainEvent(Id));
    }
}
```

*Verbatim from `src/Modules/Customer/Application/Domain/Customers/Customer.cs:1-66`.*

Three things to copy:

**`DateTimeOffset now` is the last required parameter of `Register`.** Not `DateTimeOffset.UtcNow`
inside the factory — see §2.2. The build rejects the ambient version.

**`Suspend` and `Reactivate` are idempotent by an early return.** Calling `Suspend` twice must
not raise a second `CustomerSuspendedDomainEvent`. Cheap here; the equivalent omission in
`Order.Cancel` re-raised an event *and* triggered a second inventory movement.

**The aggregate raises events but never dispatches them.** `Raise` appends to a list the service
drains after `SaveChangesAsync` (ADR-0004).

### 6.2 Value objects — and the smart-enum pattern

<!-- src/Modules/Customer/Application/Domain/ValueObjects/Address.cs:33-64 -->
```csharp
/// <summary>Smart enum — account standing drives Payment's ACL verification.</summary>
public sealed class CustomerStatus : ValueObject
{
    public static readonly CustomerStatus Active = new("Active");
    public static readonly CustomerStatus Suspended = new("Suspended");

    public string Value { get; }
    private CustomerStatus(string value) => Value = value;

    private static readonly IReadOnlyDictionary<string, CustomerStatus> Registry =
        new Dictionary<string, CustomerStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [Active.Value] = Active,
            [Suspended.Value] = Suspended,
        };

    /// <summary>
    /// Canonical, non-throwing lookup for query filters. Returns the DECLARED
    /// instance so `Where(c => c.Status == parsed)` compares against the same
    /// canonical value the converter reads back. Unknown input means no results.
    /// </summary>
    public static bool TryParse(string? value, out CustomerStatus? status)
    {
        status = string.IsNullOrWhiteSpace(value) ? null : Registry.GetValueOrDefault(value.Trim());
        return status is not null;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

*Verbatim from `src/Modules/Customer/Application/Domain/ValueObjects/Address.cs:33-64` — the
`Address` value object is in the same file, lines 1–31.*

> ⚠️ **The `Registry` is not decoration — it is half of the EF value converter.**
>
> Two defects made this pattern mandatory. First, a `Parse` that returned
> `new CustomerStatus(input)` preserved the caller's casing, so `Parse("active")` produced a value
> that compared **unequal** to the stored `"Active"` in both domain comparisons and query filters.
> Returning the *declared* instance fixes it.
>
> Second, a sibling enum was missing one case (`"Refunded"`). Because the converter runs on
> **read**, a single refunded row became **permanently unreadable** — entity loads and projections
> both threw, poisoning the payment list, the sales summary and the customer detail page. Every
> status the aggregate can persist **must** be in the registry.
>
> Provide `TryParse`, not just `Parse`: an unknown filter value should mean "no results", not a
> 500.

### 6.3 Application ports

<!-- src/Modules/Customer/Application/Ports/Inbound/ICustomerInboundPort.cs:1-42 -->
```csharp
using EwaysVNext.Contracts.Customer;
using EwaysVNext.Framework.Results;
using EwaysVNext.Framework.Security;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound;

/// <summary>
/// Inbound port — the hexagon's driving boundary. Used by BOTH the REST adapter
/// and (cross-module) Payment.Adapter.Outbound.CustomerGateway via DIRECT METHOD
/// CALLS — no events, no message bus (ADR-0002/0005).
///
/// <para>TWO AUDIENCES, TWO AUTHORIZATION MODELS. Customer-facing operations take an
/// <see cref="Actor"/> and enforce that the caller owns the profile
/// (<c>Customer.IdentityUserId</c>). The ACL-facing queries below take NO Actor: they
/// are provider lookups performed by another module inside a saga, not user-initiated
/// requests, and Payment authorizes its own caller separately. Mixing the two would
/// either block the ACL or silently weaken the user-facing check.</para>
/// </summary>
public interface ICustomerInboundPort
{
    // ── Profile lifecycle (REST-facing, ownership enforced) ──

    /// <summary>
    /// Creates a profile for <paramref name="identityUserId"/>. Only that user — or an
    /// administrator — may do so; it used to accept any identity user id from the body.
    /// </summary>
    Task<Result<Guid>> RegisterAsync(Actor actor, Guid identityUserId, string displayName, CancellationToken ct = default);

    Task<Result> AddAddressAsync(Actor actor, Guid customerId, string street, string city, string postalCode, string country, CancellationToken ct = default);

    // ── Admin-only (already gated by the "Admin" policy at every transport) ──
    Task<Result> SuspendAsync(Guid customerId, CancellationToken ct = default);
    Task<Result> ReactivateAsync(Guid customerId, CancellationToken ct = default);

    // ── Wallet (REST-facing, ownership enforced) — vault tokens only ──
    Task<Result<Guid>> SavePaymentMethodAsync(Actor actor, Guid customerId, string tokenizedCard, string cardType, DateOnly expiry, CancellationToken ct = default);

    // ── Standing & wallet (ACL-facing — consumed by Payment; NO Actor by design) ──
    Task<Result<CustomerStatusDto>> GetStatusAsync(Guid customerId, CancellationToken ct = default);
    Task<Result<SavedPaymentMethodDto>> GetSavedPaymentMethodAsync(Guid customerId, Guid paymentMethodId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SavedPaymentMethodDto>>> GetSavedPaymentMethodsAsync(Guid customerId, CancellationToken ct = default);
}
```

*Verbatim from `src/Modules/Customer/Application/Ports/Inbound/ICustomerInboundPort.cs:1-42`.*

**One port, three audiences, and the signature tells you which is which.** A method taking
`Actor` is user-initiated and ownership-checked. A method *without* one is either admin-gated at
the transport, or an ACL lookup another module performs on its own authority. **The absence of an
`Actor` is a deliberate signal, not an oversight** — so never "tidy up" by adding one uniformly:
that would block the ACL, which has no user to speak of.

### 6.4 Service — where ownership is enforced

<!-- src/Modules/Customer/Application/Service/CustomerService.cs:18-43 -->
```csharp
public sealed class CustomerService : Ports.Inbound.ICustomerInboundPort
{
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IClock _clock;

    public CustomerService(ICustomerRepository customers, IUnitOfWork unitOfWork,
        IEventDispatcher eventDispatcher, IClock clock)
        => (_customers, _unitOfWork, _eventDispatcher, _clock) = (customers, unitOfWork, eventDispatcher, clock);

    private static readonly Error CustomerNotFound = new("CUSTOMER_NOT_FOUND", "Customer was not found.");
    private static readonly Error CustomerForbidden =
        new("CUSTOMER_FORBIDDEN", "This customer profile belongs to another user.", 403);

    /// <summary>
    /// Loads the profile and asserts the actor owns it (or is an administrator). Writes
    /// authorize BEFORE mutating, which is why this happens here and not in the adapter.
    /// </summary>
    private async Task<Result<CustomerAggregate>> LoadOwnedAsync(Actor actor, Guid customerId, CancellationToken ct)
    {
        var customer = await _customers.GetByIdAsync(customerId, ct);
        if (customer is null) return Result.Failure<CustomerAggregate>(CustomerNotFound);
        if (!actor.CanActFor(customer.IdentityUserId)) return Result.Failure<CustomerAggregate>(CustomerForbidden);
        return Result.Success(customer);
    }
```

*From `src/Modules/Customer/Application/Service/CustomerService.cs:18-43`. Remaining methods
below; see the file for `RegisterAsync`, `AddAddressAsync`, `SuspendAsync`, `ReactivateAsync` and
`GetSavedPaymentMethodsAsync`.*

`LoadOwnedAsync` is the whole authorization pattern in six lines: **load, check, then hand the
aggregate to the caller.** Every ownership-checked write funnels through it, so the check cannot
be forgotten on the next method someone adds.

<!-- src/Modules/Customer/Application/Service/CustomerService.cs:101-126 -->
```csharp
    public async Task<Result<Guid>> SavePaymentMethodAsync(Actor actor, Guid customerId, string tokenizedCard, string cardType, DateOnly expiry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var owned = await LoadOwnedAsync(actor, customerId, ct);
        if (owned.IsFailure) return Result.Failure<Guid>(owned.Error);
        var customer = owned.Value!;

        SavedPaymentMethod method;
        try { method = customer.AddSavedPaymentMethod(tokenizedCard, cardType, expiry); }
        catch (DomainException dex) { return Result.Failure<Guid>(new Error(dex.Code, dex.Message)); }

        await _unitOfWork.SaveChangesAsync(ct);
        await _eventDispatcher.DispatchAndClearAsync(customer, ct); // NoOp today (ADR-0004)
        return Result.Success(method.Id);
    }

    public async Task<Result<CustomerStatusDto>> GetStatusAsync(Guid customerId, CancellationToken ct = default)
    {
        var customer = await _customers.GetByIdAsync(customerId, ct);
        return customer is null
            ? Result.Failure<CustomerStatusDto>(new Error("CUSTOMER_NOT_FOUND", $"Customer '{customerId}' was not found."))
            : Result.Success(new CustomerStatusDto(
                customer.Id, customer.IdentityUserId,
                customer.Status == CustomerStatus.Suspended, customer.AccountTier));
    }
```

*Verbatim from `src/Modules/Customer/Application/Service/CustomerService.cs:101-126`.*

Note the shape: **authorize → mutate via the aggregate → save → dispatch.** The
`catch (DomainException)` converts a domain refusal into an `Error` rather than letting it become
a 500 (§2.5) — the service is the layer where exceptions become results.

`GetStatusAsync` takes **no** `Actor`: it is the ACL entry point. It still returns
`IdentityUserId` so the *consumer* can authorize.

### 6.5 Outbound adapter — registration

<!-- src/Modules/Customer/Adapter/Outbound/DependencyInjection.cs:11-19 -->
```csharp
{
    public static IServiceCollection AddCustomerOutboundAdapters(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        services.AddModuleDbContext<CustomerDbContext>(config, "Customer", "Customer", "__EFMigrationsHistory_Customer", isDevelopment);
        services.AddScoped<Application.Ports.Outbound.ICustomerRepository, CustomerRepository>();
        services.AddScoped<Application.Ports.Outbound.IUnitOfWork, CustomerUnitOfWork>();
        return services;
    }
}
```

*Verbatim from `src/Modules/Customer/Adapter/Outbound/DependencyInjection.cs:11-19`.*

> ⚠️ **Use `AddModuleDbContext<T>`, never a hand-rolled `AddDbContext`.** The shared helper
> supplies the per-module migrations-history table, the "refuse to fall back to InMemory outside
> Development" guard, and `EnableRetryOnFailure`. Brand and Supplier copied a hand-rolled block
> and silently missed retry when it was added (§2.4). The fourth argument
> (`__EFMigrationsHistory_Customer`) must also be set on the module's **design-time factory**, or
> `dotnet ef database update` records history in a different table than the app reads.

---

## 7. Step 3 — consumer module: **Payment**

### 7.1 Domain — `Money`, time, and idempotency

<!-- src/Modules/Payment/Application/Domain/Payments/PaymentTransaction.cs:8-47 -->
```csharp
public sealed class PaymentTransaction : AggregateRoot<Guid>
{
    public Guid CustomerId { get; private set; }
    public Guid OrderId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public PaymentStatus Status { get; private set; } = null!;
    public PaymentMethodSnapshot Method { get; private set; } = null!; // token snapshot, never a PAN
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public string? FailureReason { get; private set; }
    /// <summary>Client-supplied idempotency key (nullable). Unique when present.</summary>
    public string? ClientRequestId { get; private set; }

    private PaymentTransaction() { }

    /// <summary>Caller must verify customer standing BEFORE calling this factory (saga step 1).</summary>
    public static PaymentTransaction Initiate(Guid customerId, Guid orderId, Money amount,
        PaymentMethodSnapshot method, DateTimeOffset now, string? clientRequestId = null)
    {
        if (customerId == Guid.Empty || orderId == Guid.Empty)
            throw new DomainException("PARTY_IDS_REQUIRED", "Customer and order ids are required.");
        if (amount.Amount <= 0)
            throw new DomainException("AMOUNT_POSITIVE_REQUIRED", "Amount must be positive.");
        if (clientRequestId is { Length: > 100 })
            throw new DomainException("CLIENT_REQUEST_ID_INVALID", "ClientRequestId must be at most 100 characters.");

        var tx = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            OrderId = orderId,
            Amount = amount,
            Method = method,
            Status = PaymentStatus.Pending,
            CreatedAt = now,
            ClientRequestId = string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId.Trim(),
        };
        tx.Raise(new PaymentInitiatedDomainEvent(tx.Id, customerId, amount.Amount, amount.Currency));
        return tx;
    }
```

*From `src/Modules/Payment/Application/Domain/Payments/PaymentTransaction.cs:8-47`. State
transitions follow at lines 49-80; see the file.*

**`Amount` is `Money`, not `decimal`.** An amount without its currency is how a EUR capture came
to satisfy a USD order (§2.9). `Money` also refuses currencies it cannot represent:

<!-- src/BuildingBlocks/SharedKernel/ValueObjects/Money.cs:62-78 -->
```csharp
    public static Money Create(decimal amount, string currency = "USD")
    {
        if (amount < 0) throw new DomainException("MONEY_NEGATIVE", "Money amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("MONEY_CURRENCY_INVALID", "Currency must be a 3-letter ISO code.");

        var code = currency.ToUpperInvariant();
        if (NonTwoDecimalCurrencies.Contains(code))
            throw new DomainException("MONEY_UNSUPPORTED_CURRENCY_DECIMALS",
                $"'{code}' does not use two decimal places, and this template rounds every amount " +
                "to two. Supporting it requires a per-currency minor-unit exponent and matching " +
                "storage precision. Refusing rather than silently mis-rounding the amount.");

        // MidpointRounding.ToEven (the default) is deliberate: "banker's rounding" does not
        // drift upward across many transactions the way AwayFromZero does.
        return new Money(Math.Round(amount, SupportedDecimals, MidpointRounding.ToEven), code);
    }
```

*Verbatim from `src/BuildingBlocks/SharedKernel/ValueObjects/Money.cs:62-78`.*

> ⚠️ **`Money` supports two-decimal currencies only.** JPY and KRW have no minor unit; KWD, BHD,
> OMR and TND have three. Rather than silently turning 1.234 KWD into 1.23 KWD, `Create` throws.
> If your module handles those currencies you need a per-currency exponent first — do not
> "fix" it by widening the rounding.

**`CreatedAt = now`, not `DateTimeOffset.UtcNow`** (§2.2), and **`ClientRequestId` is stored on
the aggregate** (§2.6) so it can reach the index in §7.4.

### 7.2 Ports — the consumer-owned gateway port

<!-- src/Modules/Payment/Application/Ports/Outbound/ICustomerGatewayPort.cs:1-22 -->
```csharp
using EwaysVNext.Contracts.Customer;
using EwaysVNext.Framework.Results;

namespace EwaysVNext.Modules.Payment.Application.Ports.Outbound;

/// <summary>
/// SYNCHRONOUS ACL port — Payment asks Customer within one logical operation, but NOT within
/// one database transaction: the modules own separate DbContexts and connections, so a
/// cross-module operation cannot roll back atomically (ADR-0002's "same transaction" claim is
/// withdrawn; ADR-0005 has the compensation model).
/// NO events, NO message bus. Implemented by
/// CustomerGateway in Adapter/Outbound/Gateways, which delegates DIRECTLY to
/// Customer's ICustomerInboundPort. Uses ONLY Contracts DTOs.
///
/// NOTE: these are read-only queries against the provider; no compensation steps
/// are needed for them (nothing to roll back on Customer's side).
/// </summary>
public interface ICustomerGatewayPort
{
    Task<Result<CustomerStatusDto>> GetCustomerStatusAsync(Guid customerId, CancellationToken ct = default);
    Task<Result<SavedPaymentMethodDto>> GetSavedPaymentMethodAsync(Guid customerId, Guid paymentMethodId, CancellationToken ct = default);
}
```

*Verbatim from `src/Modules/Payment/Application/Ports/Outbound/ICustomerGatewayPort.cs:1-22`.*

**The port is defined by the CONSUMER and speaks only Contracts DTOs.** Payment's hexagon has no
idea `ICustomerInboundPort` exists. That is what makes the boundary testable: swap the gateway
for a fake and Payment's tests need no Customer assembly at all.

The inbound port shows the `Actor`-first convention on every customer-facing operation:

<!-- src/Modules/Payment/Application/Ports/Inbound/IPaymentService.cs:6-24 -->
```csharp
public interface IPaymentService
{
    Task<Result<Guid>> InitiateWithSavedCardAsync(
        Actor actor, Guid customerId, Guid orderId, decimal amount, string currency,
        Guid savedPaymentMethodId, string? clientRequestId = null, CancellationToken ct = default);

    Task<Result<Guid>> InitiateWithNewCardTokenAsync(
        Actor actor, Guid customerId, Guid orderId, decimal amount, string currency,
        string cardToken, string cardType, DateOnly expiry, string? clientRequestId = null, CancellationToken ct = default);

    Task<Result> EnsureCustomerOwnedByActorAsync(Actor actor, Guid customerId, CancellationToken ct = default);

    Task<Result<OrderPaymentStatusDto>> GetOrderPaymentStatusAsync(Guid orderId, CancellationToken ct = default);

    Task<Result> CaptureAsync(Guid paymentId, CancellationToken ct = default);
    Task<Result> FailAsync(Guid paymentId, string reason, CancellationToken ct = default);
    Task<Result> RefundAsync(Guid paymentId, CancellationToken ct = default);
}
```

*Condensed from `src/Modules/Payment/Application/Ports/Inbound/IPaymentService.cs:6-24` —
XML docs removed, signatures verbatim.*

`CaptureAsync`/`FailAsync`/`RefundAsync` take no `Actor`: they are operator actions behind the
Admin policy (§7.6).

### 7.3 Service — the saga in practice

This is the method the whole guide builds to. It is reproduced in full because every line carries
a decision.

<!-- src/Modules/Payment/Application/Service/PaymentService.cs:61-125 -->
```csharp
    public async Task<Result<Guid>> InitiateWithSavedCardAsync(
        Actor actor, Guid customerId, Guid orderId, decimal amount, string currency,
        Guid savedPaymentMethodId, string? clientRequestId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        // Idempotency (ADR-0005 follow-up): a replayed client key returns the
        // ORIGINAL transaction — a naive network retry can no longer double-charge.
        if (!string.IsNullOrWhiteSpace(clientRequestId))
        {
            var existing = await _transactions.GetByClientRequestIdAsync(clientRequestId, ct);
            if (existing is not null)
            {
                _logger.LogInformation("Idempotent replay for {Key}: returning existing payment {PaymentId}",
                    clientRequestId, existing.Id);
                return Result.Success(existing.Id);
            }
        }
        CustomerStatusResult? status = null;
        SavedMethodResult? method = null;
        PaymentTransaction? transaction = null;

        var saga = new CrossModuleSaga(_logger, $"payment-initiate:saved-card:{customerId}")
            // Step 1 both AUTHORIZES and validates standing. One ACL fetch answers both
            // questions, and this is the first step, so nothing is mutated before the
            // ownership check. Authorization is evaluated before the business rule, so a
            // non-owner learns nothing about whether the account is suspended.
            .Step("authorize-customer-and-verify-standing",
                async token =>
                {
                    var r = await _customers.GetCustomerStatusAsync(customerId, token);
                    if (!r.IsSuccess) return Result.Failure(r.Error);
                    if (!actor.CanActFor(r.Value!.IdentityUserId)) return Result.Failure(CustomerForbidden);

                    status = new CustomerStatusResult(r.Value.IsSuspended);
                    return r.Value.IsSuspended
                        ? Result.Failure(new Error("CUSTOMER_SUSPENDED", "Account is suspended; payment refused."))
                        : Result.Success();
                })
            .Step("fetch-saved-card-token",
                async token =>
                {
                    var m = await _customers.GetSavedPaymentMethodAsync(customerId, savedPaymentMethodId, token);
                    if (!m.IsSuccess) return Result.Failure(m.Error);
                    if (m.Value!.ExpiryDate < DateOnly.FromDateTime(DateTime.UtcNow))
                        return Result.Failure(new Error("CARD_EXPIRED", "The selected card has expired."));
                    method = new SavedMethodResult(m.Value);
                    return Result.Success();
                })
            .Step("persist-transaction",
                async token =>
                {
                    // Race-safe: unique-index losers resolve to the winning transaction.
                    transaction = await _transactions.AddOrGetByClientRequestIdAsync(
                        PaymentTransaction.Initiate(
                            customerId, orderId, Money.Create(amount, currency),
                            PaymentMethodSnapshot.FromSaved(method!.Dto), _clock.UtcNow, clientRequestId),
                        token);
                    await _eventDispatcher.DispatchAndClearAsync(transaction, ct); // NoOp today (ADR-0004)
                    return Result.Success();
                });

        var sagaResult = await saga.ExecuteAsync(ct);
        if (sagaResult.IsFailure) return Result.Failure<Guid>(sagaResult.Error);
        return Result.Success(transaction!.Id);
    }
```

*Verbatim from `src/Modules/Payment/Application/Service/PaymentService.cs:61-125`.*

Five decisions worth copying:

1. **The idempotency pre-check runs before the saga.** A sequential replay must not re-run steps
   that have side effects. The concurrent case is handled separately, at the index (§7.4).
2. **Step 1 authorizes and validates standing in one ACL call**, and it is *first*. Nothing is
   mutated before ownership is known. Authorization is evaluated **before** the business rule, so
   a non-owner cannot learn whether an account is suspended — an ordering detail with a security
   consequence.
3. **No step here registers a compensation, because every cross-module step is a read.** That is
   the honest reason, and it is stated in the port's own doc comment. Do not add empty
   compensations for symmetry.
4. **The write is the last step.** Reads that can refuse come first (§1).
5. **Closure variables carry state between steps** (`status`, `method`, `transaction`). Each step
   is a `Func<CancellationToken, Task<Result>>`, so results do not flow through return values.

The step signature matters:

<!-- src/BuildingBlocks/Framework/Sagas/CrossModuleSaga.cs:20-23 -->
```csharp
public sealed record SagaStep(
    string Name,
    Func<CancellationToken, Task<Result>> Action,
    Func<CancellationToken, Task<Result>>? Compensation = null);
```

*Verbatim from `src/BuildingBlocks/Framework/Sagas/CrossModuleSaga.cs:20-23`.*

> ⚠️ **`Compensation` returns `Task<Result>`, not `Task`.** As a bare `Task`, `Task<Result>`
> converted implicitly and every real compensation **silently discarded its own failure** — and
> providers report failure by *returning* `Result.Failure`, so the only failure mode that occurs
> was the one nobody could see. Returning `Result` makes it unignorable at the type level.

> ⚠️ **Compensation is targeted by INDEX, not by name.** Name-based lookup misroutes whenever two
> steps share a name — and a place-order saga names its steps `reserve-stock:{productId}`, so one
> product on two lines compensated the first step twice and never released the second.

### 7.4 Idempotency — the aggregate, the index, and the race

The pre-check in §7.3 handles a *sequential* replay. A *concurrent* duplicate is caught by the
database:

<!-- src/Modules/Payment/Adapter/Outbound/Repositories/SqlServer/Configurations/PaymentTransactionConfiguration.cs:20-22 -->
```csharp
        // Idempotency (ADR-0005 follow-up): at most one transaction per client key.
        // Filtered index — SQL Server unique indexes would otherwise allow only one NULL.
        builder.HasIndex(t => t.ClientRequestId).IsUnique().HasFilter("[ClientRequestId] IS NOT NULL");
```

*Verbatim from `…/PaymentTransactionConfiguration.cs:20-22`.*

> ⚠️ **The filter is mandatory, not an optimisation.** A plain unique index on a nullable column
> permits exactly **one** NULL row in SQL Server — so the second payment without an idempotency
> key would fail. `HasFilter("[Col] IS NOT NULL")` is the pattern; `Order.ClientRequestId` uses
> the identical shape.

The repository turns the losing side of the race into a success:
`AddOrGetByClientRequestIdAsync` catches the unique-violation and returns the **winner's**
transaction, so the caller is idempotent from the outside.

### 7.5 Outbound adapter — the gateway

<!-- src/Modules/Payment/Adapter/Outbound/Gateways/CustomerGateway.cs:1-21 -->
```csharp
using EwaysVNext.Contracts.Customer;
using EwaysVNext.Framework.Results;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound; // ← EXCEPTION #2 (csproj reference)
using EwaysVNext.Modules.Payment.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

/// <summary>
/// ACL adapter — implements Payment's OWN ICustomerGatewayPort by delegating
/// DIRECTLY to Customer's ICustomerInboundPort. Same process, synchronous,
/// read-only queries against the provider — no compensation needed on failure.
/// NO events, NO message bus, NO Wolverine (ADR-0002/0005).
/// </summary>
internal sealed class CustomerGateway(ICustomerInboundPort customerPort) : ICustomerGatewayPort
{
    public Task<Result<CustomerStatusDto>> GetCustomerStatusAsync(Guid customerId, CancellationToken ct = default)
        => customerPort.GetStatusAsync(customerId, ct);

    public Task<Result<SavedPaymentMethodDto>> GetSavedPaymentMethodAsync(Guid customerId, Guid paymentMethodId, CancellationToken ct = default)
        => customerPort.GetSavedPaymentMethodAsync(customerId, paymentMethodId, ct);
}
```

*Verbatim from `src/Modules/Payment/Adapter/Outbound/Gateways/CustomerGateway.cs:1-21`.*

**Twenty-one lines, and every one of them is delegation.** The gateway is `internal sealed` and
contains no caching, no retry loop and no business decision. If you find yourself adding a
conditional here, it belongs in the service. Registered as:

```csharp
services.AddScoped<Application.Ports.Outbound.ICustomerGatewayPort, Gateways.CustomerGateway>();
```

### 7.6 Inbound adapter — who may call what

> ⚠️ **`POST /payments/saved-card` and `POST /payments/new-card-token` are `RequireAuthorization("Admin")`.**
> They accept a **caller-supplied amount**, and a payment whose amount the caller chooses is a
> payment that can settle a $1,000 order for one cent.
>
> The **customer-facing** path is `POST /orders/{id}/pay`, which takes **no amount** — Orders
> charges `order.TotalAmount` from its own aggregate. `/payments/{id}/capture` and `/fail` are
> Admin-only for the same reason: they are operator actions.

Server faults must be declared, not inferred:

```csharp
// Error's status heuristic deliberately defaults to 400: an unrecognised code is more
// likely a client mistake than a server condition. So a genuine SERVER fault must say so
// explicitly at the site that creates it — otherwise a deadlock or a null-returning saga
// step is reported as "your request was bad" and is invisible to every 5xx alert.
new Error("SAGA_STEP_FAILED", $"Step '{step.Name}' failed: {ex.Message}", 500)
```

*Pattern from `src/BuildingBlocks/Framework/Sagas/CrossModuleSaga.cs`; see `ResultHttpSemanticsTests` for the full status table.*

---

## 8. Step 4 — the read side (`QueryApplication`)

**RULES:** no writes, no aggregate loading with behaviour, no domain rules — just
`Set<T>().Where(...).Select(...)` into Contract records. This is "Read-Optimized CQS" (ADR-0008):
the **same database**, synchronous projections, no event propagation and no eventual consistency.

### 8.1 The predicate/projection asymmetry

<!-- src/Modules/Customer/QueryApplication/CustomerQueryService.cs:27-46 -->
```csharp
internal sealed class CustomerQueryService(CustomerDbContext db) : ICustomerQueryService
{
    public async Task<Result<CustomerSnapshot>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var result = await db.Set<CustomerAggregate>()
            .Where(c => c.Id == id)
            .Select(c => new CustomerSnapshot(c.Id, c.IdentityUserId, c.DisplayName, c.Status.Value, c.AccountTier))
            .FirstOrDefaultAsync(ct);

        return result is null
            ? Result.Failure<CustomerSnapshot>(new Error("CUSTOMER_NOT_FOUND", "Customer was not found."))
            : Result.Success(result);
    }

    public async Task<IReadOnlyList<CustomerSnapshot>> ListAsync(int page, int pageSize, CancellationToken ct = default)
        => await db.Set<CustomerAggregate>()
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new CustomerSnapshot(c.Id, c.IdentityUserId, c.DisplayName, c.Status.Value, c.AccountTier))
            .ToListAsync(ct);
```

*From `src/Modules/Customer/QueryApplication/CustomerQueryService.cs:27-46`. `GetByIdsAsync` and
`SearchCustomersAsync` follow; the latter is quoted below.*

Note the constructor takes **`CustomerDbContext`**, not the abstract `DbContext` — the read side
is scoped to its own module's context.

<!-- src/Modules/Customer/QueryApplication/CustomerQueryService.cs:57-83 -->
```csharp
    public async Task<IReadOnlyList<CustomerSnapshot>> SearchCustomersAsync(string? search, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Set<CustomerAggregate>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.DisplayName.Contains(term));
        }
        // Predicates compare the WHOLE converted property against a value object;
        // EF applies the converter to the parameter. Reaching through it
        // (c.Status.Value) is untranslatable on SQL Server. Still safe in the
        // projection below, where EF reads the column and converts client-side.
        // An unrecognised status matches nothing, as it always has.
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!CustomerStatus.TryParse(status, out var parsed)) return [];
            query = query.Where(c => c.Status == parsed);
        }

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new CustomerSnapshot(c.Id, c.IdentityUserId, c.DisplayName, c.Status.Value, c.AccountTier))
            .ToListAsync(ct);
    }
```

*Verbatim from `src/Modules/Customer/QueryApplication/CustomerQueryService.cs:57-83`.*

> ⚠️ **The single most expensive lesson in this codebase.**
>
> `c.Status.Value` appears **twice** in this method with opposite legality:
>
> | Position | Legal? | Why |
> |---|---|---|
> | `.Where(c => c.Status.Value == status)` | ❌ **No** | Reaching *through* a `HasConversion` property is untranslatable on SQL Server |
> | `.Where(c => c.Status == parsed)` | ✅ Yes | Compare the **whole** property; EF applies the converter to the parameter |
> | `.Select(c => … c.Status.Value …)` | ✅ Yes | EF reads the column and converts client-side |
>
> `EF.Property<string>(c, "Status")` is **also wrong** — the property's CLR type is
> `CustomerStatus`, so it throws `InvalidCastException` on *both* providers.
>
> This broke **login**. Eleven sites used the illegal form, and every test passed because EF
> InMemory evaluates untranslatable LINQ client-side. §10.2 is how you catch it.

### 8.2 Project columns, never computed properties

> ⚠️ **Never reference an unmapped computed property in a projection.** An admin query projected
> `p.AvailableStock` — a `=> Stock - ReservedStock` expression with no column. EF abandoned the
> flat projection and materialised the whole aggregate, **including its owned collection via a
> LEFT JOIN, tracked**, to render one row. Write `p.Stock - p.ReservedStock` instead.
>
> `ReadSideProjectionTests` catches this by asserting `db.ChangeTracker.Entries()` is **empty**
> after a read — the direct symptom, and one that cannot drift the way a reconstructed query can.

### 8.3 Bound the page at the boundary — reject, do not clamp

<!-- src/Modules/Admin/Application/Service/FailureReportService.cs:130-144 -->
```csharp
    private static Error? Validate(FailureReportFilter filter)
    {
        if (filter.Page < 1) return new Error("REPORT_PAGE_INVALID", "Page must be >= 1.");
        if (filter.PageSize is < 1 or > MaxPageSize) return new Error("REPORT_PAGE_SIZE_INVALID", $"PageSize must be between 1 and {MaxPageSize}.");
        if (filter.From is not null && filter.To is not null && filter.From > filter.To)
            return new Error("REPORT_RANGE_INVALID", "'From' must not be after 'To'.");
        if (filter.MinAmount is < 0) return new Error("REPORT_MINAMOUNT_INVALID", "MinAmount must be >= 0.");
        if (filter.CustomerStatus is not null
            && !filter.CustomerStatus.Equals("Active", StringComparison.OrdinalIgnoreCase)
            && !filter.CustomerStatus.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            return new Error("REPORT_STATUS_INVALID", "CustomerStatus must be 'Active', 'Suspended' or omitted.");

        return null;
    }
```

*Verbatim from `src/Modules/Admin/Application/Service/FailureReportService.cs:130-144`.*

> ⚠️ **Reject, do not silently clamp.** `page: 0` reaching `.Skip((page - 1) * pageSize)` emits
> `OFFSET -20 ROWS`, which SQL Server rejects — so a *client* mistake surfaces as a *server*
> error. And `pageSize: 1000000` is an unbounded scan available to anyone authenticated.
>
> Clamping would hide the mistake; the caller would page through data believing it asked for
> something it did not. A typed error tells them.
>
> Applies to **every** paged surface. Four admin resolvers were identical copies of the same
> unguarded call, so a fix applied to three would have looked green in any test that sampled one.

### 8.4 Cap the scan, not the result

> ⚠️ **A cap applied after materialisation is not a cap.** A report declared
> `MaxRows = 5000 // hard guard against unbounded scans` and then applied it **client-side, after
> fetching every matching row**. Push the bound into the query — `.Take(take)` on the
> `IQueryable`, before `ToListAsync` — and make a non-positive value return nothing rather than
> everything.

---

## 9. Step 5 — composition root

<!-- src/Modules/Customer/Installer/ModuleInstaller.cs:1-20 -->
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Endpoints;
using EwaysVNext.Modules.Customer.Adapter.Outbound;
using EwaysVNext.Modules.Customer.Application;
using EwaysVNext.Modules.Customer.QueryApplication;

namespace EwaysVNext.Modules.Customer;

public static class ModuleInstaller
{
    public static IServiceCollection AddCustomerModule(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        services.AddCustomerApplication();            // ICustomerInboundPort → CustomerService (sealed)
        services.AddCustomerOutboundAdapters(config, isDevelopment); // DbContext, repos, IUnitOfWork
        services.AddCustomerRestAdapter();            // IEndpoint scan (Scrutor)
        services.AddCustomerQueryApplication();
        return services;
    }
}
```

*Verbatim from `src/Modules/Customer/Installer/ModuleInstaller.cs:1-20`. `AddPaymentModule` has
the same shape and additionally registers `ICustomerGatewayPort → CustomerGateway`; see
`src/Modules/Payment/Installer/ModuleInstaller.cs`.*

The `isDevelopment` flag is not cosmetic: it is what lets `AddModuleDbContext` **refuse** to fall
back to the InMemory provider outside Development rather than silently losing data.

Each module's `Application/DependencyInjection.cs` registers the clock defensively:

```csharp
// TryAdd, not Add: every module needs the clock and each installer must stay
// independently composable, so whichever runs first wins and none of them
// double-registers. The domain never sees IClock — services read it ONCE per
// operation and pass the timestamp into the aggregate.
services.TryAddSingleton<IClock, SystemClock>();
```

*Pattern from every `src/Modules/*/Application/DependencyInjection.cs`.*

Host wiring — order matters:

<!-- src/Host/EwaysVNext.Host/Program.cs:54-60 -->
```csharp
builder.Services
    .AddIdentityModule(builder.Configuration, builder.Environment.IsDevelopment())
    .AddCatalogModule(builder.Configuration, builder.Environment.IsDevelopment())
    .AddOrdersModule(builder.Configuration, builder.Environment.IsDevelopment())
    .AddCustomerModule(builder.Configuration, builder.Environment.IsDevelopment()) // provider BEFORE consumer
    .AddPaymentModule(builder.Configuration, builder.Environment.IsDevelopment()) // consumer resolves ICustomerInboundPort
```

*From `src/Host/EwaysVNext.Host/Program.cs:54-60` (abridged to the relevant registrations).*

Then health checks (`.AddDbContextCheck<CustomerDbContext>("Customer DB")`), endpoint mapping
(`app.MapCustomerRestEndpoints()`), and `ConnectionStrings__Customer` /
`ConnectionStrings__Payment` in configuration — one database per module, always.

> **The host never migrates on startup.** Apply migrations explicitly with
> `dotnet run --project src/Host/EwaysVNext.Host -- --migrate`. Prefer that over
> `dotnet ef database update`, which uses the design-time factory.

---

## 10. Verification — proving the module works

Four tiers. Add them in this order; each catches what the previous cannot.

> **Read [`TESTING-GUARANTEES.md`](TESTING-GUARANTEES.md) before trusting a green run.** This
> repository once had **95 passing tests on a system that could not log a user in against SQL
> Server**. The tests were not weak — they were confidently testing the wrong runtime.

### 10.1 Architecture tests — extend the theories, do not add copies

The boundary rules are **parameterised theories**, not one test per module pair. Adding a new
module means adding *data*, not a test.

<!-- tests/ArchitectureTests/HexagonalBoundaryTests.cs:107-119 -->
```csharp
    public void No_Module_Application_Can_Reference_Another_Module_Application()
    {
        foreach (var consumer in ModuleNames)
            foreach (var provider in ModuleNames.Where(n => n != consumer))
            {
                var result = Types.InAssembly(AppAssembly(consumer))
                    .ShouldNot().HaveDependencyOn($"EwaysVNext.Modules.{provider}")
                    .GetResult();
                result.IsSuccessful.Should().BeTrue(
                    $"{consumer}.Application must NOT depend on {provider}.* — module boundary violation. " +
                    "Cross-module communication only through Contracts + ACL ports.");
            }
    }
```

*Verbatim from `tests/ArchitectureTests/HexagonalBoundaryTests.cs:107-119.* Adding your module to
`ModuleNames` gives you the full N×N matrix for free.*

A sanctioned ACL exception is **one `InlineData` line**:

<!-- tests/ArchitectureTests/HexagonalBoundaryTests.cs:122-136 -->
```csharp
    /// <summary>
    /// The ONLY sanctioned cross-module Application references are the ACL gateway
    /// adapters: Orders→Catalog and Payment→Customer. Every other assembly
    /// (including all Applications and all other Adapters) is forbidden.
    /// </summary>
    [Theory]
    [InlineData("Catalog", "Orders")]
    [InlineData("Customer", "Payment")]
    // Orders orchestrates payment for its own orders so the charge amount comes from the
    // Order aggregate. ONE direction: Payment references Orders nowhere, which this same
    // theory proves — Payment.Application appears in forbiddenAssemblies for this row.
    [InlineData("Payment", "Orders")]
    public void Only_The_Sanctioned_Gateway_Adapter_May_Reference_Provider_Application(string provider, string consumer)
```

*Verbatim from `tests/ArchitectureTests/HexagonalBoundaryTests.cs:122-136`; the body builds the
forbidden-assembly set and is at lines 137-159.*

> ⚠️ **Do not hand-write `Payment_Must_Not_Depend_On_Customer`-style tests.** An earlier version
> of this guide instructed exactly that, for three tests that do not exist — the theories replaced
> them. Duplicating coverage in a differently-named test is how a suite grows without getting
> stronger.

### 10.2 Persistence contract tests — no database required

`PersistenceContractTests` runs **the real production query methods** through DI against **two
providers**, because neither alone sees the whole mapping contract:

| Provider | Configured as | Catches |
|---|---|---|
| **SQL Server** | pointed at a refused port | **Translation** failures — happens before connection, so an untranslatable query throws a *translation* error while a translatable one throws a *connection* error |
| **EF InMemory** | actually executes | **Materialization** failures — a converter that cannot parse a stored value, which the connection error would mask |

The classifier is an **allowlist**: a `DbException` anywhere in the chain means "reached the
database", i.e. the shape translated. An earlier version used a message *denylist* and passed a
genuinely broken fix.

Add one line per query method to the relevant theory. A filter left `null` makes the assertion
vacuous — always pass a non-null value for every optional parameter.

### 10.3 Integration tests — real SQL Server

`IntegrationTests` runs the full HTTP host on **SQL Server LocalDB**, with the schema built by
**running the migrations**:

```bash
sqllocaldb start MSSQLLocalDB
```

Four guarantees are only observable here: optimistic concurrency (InMemory generates no
`rowversion`), check constraints (InMemory ignores them), migration data scripts (InMemory has no
migrations), and deadlock survival via `EnableRetryOnFailure` (InMemory has no execution
strategy).

> ⚠️ **Supply connection strings through *configuration*, not by overriding `DbContextOptions`.**
> Five test classes each carried a `Replace<TContext>(…UseInMemoryDatabase…)` block, which
> bypassed the production registration entirely — including `EnableRetryOnFailure` and the
> migrations-history table. The system under test was not the system that ships.

> ⚠️ **The dataset shape is part of the test.** A defect where Customer 360 filtered in memory
> after a global page was invisible to a six-row fixture and obvious at sixty. If a bound exists,
> seed **past** it.

### 10.4 Mutation testing — the standard, not an extra

**A guard that has not been mutation-tested is not known to work.** For each guard you add:

1. Reintroduce the bug.
2. Prove a **named** test fails.
3. Restore.
4. Prove green.

> ⚠️ **Verify the mutation actually landed.** A patch that silently fails to apply looks
> *identical* to a test gap. Assert a count changed before believing "not caught".

> ⚠️ **A surviving mutation is a finding about your tests, not a pass.** Three real examples from
> this codebase: pinning a currency back to a hardcoded `"USD"` survived because every fixture
> priced in USD; ignoring an aggregate's timestamp parameter survived because every test asserted
> *set membership* rather than *ordering*; and an end-to-end test declared its own copy of a
> response DTO, so it had never asserted the production shape at all.

Finally, run **both configurations**. `Release` enables `TreatWarningsAsErrors` and caught seven
regressions during the hardening of this template:

```bash
dotnet build EwaysVNext.slnx -c Release && dotnet test EwaysVNext.slnx -c Release
```

### 10.5 Manual review checklist

NetArchTest cannot see these:

- [ ] Only the consumer's `Adapter.Outbound.csproj` references the provider's `Application.csproj`.
- [ ] No `Wolverine`/`MediatR`/`MassTransit` package in any of the ten csproj.
- [ ] No raw card numbers: grep for `PAN|CardNumber|card_number` — only `Token`, `TokenizedCard`,
      `Last4` allowed. Domain factories enforce the `tok_` prefix.
- [ ] `QueryApplication` classes contain only `Select` projections — no `Add/Update/Remove`, no
      `SaveChanges`, no reference to a write-side outbound port.
- [ ] The gateway is `internal sealed` and pure delegation — no caching, no retry, no decisions.
- [ ] Every customer-facing inbound port takes an `Actor`; every admin-only one deliberately does
      not.
- [ ] No `DateTimeOffset.UtcNow` anywhere under `Application/Domain`.
- [ ] The design-time factory sets the **same** migrations-history table as `AddModuleDbContext`.
- [ ] Modules registered in the host **after** their dependencies; health checks and endpoint maps
      updated.

---

## 11. Pitfall index

Every row is a defect that existed in this repository.

| Symptom | Cause | Where |
|---|---|---|
| Works on InMemory, throws on SQL Server | `x.Vo.Value ==` in a **predicate** | §8.1 |
| `InvalidCastException` on both providers | `EF.Property<string>` on a converted property | §8.1 |
| One row makes a whole list endpoint throw | Smart enum missing a persisted case | §6.2 |
| Filter matches nothing for valid input | `Parse` returned a non-canonical instance | §6.2 |
| A read materialises the whole aggregate | Projected an unmapped computed property | §8.2 |
| `OFFSET -20 ROWS` | `page` unvalidated | §8.3 |
| A "hard cap" that does not cap | `Take` applied after materialisation | §8.4 |
| Any user can act as any other | Inbound port takes a subject id, not an `Actor` | §6.3, §7.2 |
| Non-owner learns account state | Business rule evaluated before authorization | §7.3 |
| Retry double-charges | `clientRequestId` validated then dropped | §7.4 |
| Second row without an idempotency key fails | Unique index not filtered on `IS NOT NULL` | §7.4 |
| Compensation failure invisible | `Compensation` typed `Task`, not `Task<Result>` | §7.3 |
| Wrong step compensated | Compensation targeted by name, not index | §7.3 |
| Deadlock becomes a business failure | `EnableRetryOnFailure` missing — hand-rolled `AddDbContext` | §6.5 |
| Server fault reported as `400` | Relied on the status heuristic | §7.6 |
| Healthy server returns `500` | A `Result`-returning method threw for a refusal | §2.5 |
| Wrong-currency payment satisfies an order | Amount compared without its currency | §7.1 |
| Amount silently mis-rounded | Non-2dp currency (JPY/KWD) | §7.1 |
| Test can only fake time with zero-length TTLs | Aggregate reads the ambient clock | §2.2 |
| `dotnet ef database update` re-applies everything | Design-time factory omits the history table | §6.5 |

---

## Appendix — canonical references

| Concept | File |
|---|---|
| `Actor` + `CanActFor` | `src/BuildingBlocks/Framework/Security/Actor.cs` |
| `IClock` / `SystemClock` | `src/BuildingBlocks/Framework/Common/IClock.cs` |
| `Money` | `src/BuildingBlocks/SharedKernel/ValueObjects/Money.cs` |
| `Result` / `Error` + HTTP mapping | `src/BuildingBlocks/Framework/Results/Result.cs` |
| `CrossModuleSaga`, `SagaStep`, `SagaSignals` | `src/BuildingBlocks/Framework/Sagas/CrossModuleSaga.cs` |
| `AddModuleDbContext<T>` | `src/BuildingBlocks/Infrastructure.Persistence/Persistence/EfCoreBase.cs` |
| Boundary rules | `tests/ArchitectureTests/HexagonalBoundaryTests.cs` |
| Ambient-clock guard | `tests/ArchitectureTests/DeterministicTimeTests.cs` |
| Dual-provider harness | `tests/PersistenceContractTests/` |
| LocalDB host | `tests/IntegrationTests/LocalDbFixture.cs` |
| What the tests do **not** prove | `docs/TESTING-GUARANTEES.md` |
| Decisions and their amendments | `docs/adr/` |
