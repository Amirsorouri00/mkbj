using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Payment;
using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Ports.Inbound;
using EwaysVNext.Modules.Payment.Application.Service;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.PaymentTests;

public class PaymentGatewayTests
{
    private static readonly Uri CallbackUrl = new("https://merchant.example/payment/callback");
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, string> NoFields = new();
    private static readonly string ServiceCategory = typeof(PaymentGatewayService).FullName!;

    [Fact]
    public async Task Default_provider_success_stops_before_fallback_and_returns_post_fields()
    {
        var first = new TestProvider("first");
        var second = new TestProvider("second");
        using var services = CreateServices([second, first]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(GatewayAttempt.Create("first", 1001, "first-token", RequestedAt), result.Value!.Attempt);
        Assert.Equal(42, first.LinkRequests.Single().BankTransactionId);
        Assert.Equal(120m, first.LinkRequests.Single().Amount.Amount);
        Assert.Equal(GatewayRedirectMethod.Post, result.Value.Redirect.Method);
        Assert.Equal("first-token", result.Value.Redirect.Fields["RefId"]);
        Assert.Empty(second.LinkRequests);
    }

    [Fact]
    public async Task Failure_tries_fallback_with_a_new_request_reference_for_the_same_transaction()
    {
        var first = new TestProvider("first") { Create = (_, _) => Task.FromResult(Result.Failure<GatewayLink>(new Error("DECLINED", "Unavailable"))) };
        var second = new TestProvider("second");
        using var services = CreateServices([first, second]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("second", result.Value!.Attempt.ProviderName);
        Assert.Equal(1002, result.Value.Attempt.RequestId);
        Assert.Equal(1001, first.LinkRequests.Single().RequestId);
        Assert.Equal(1002, second.LinkRequests.Single().RequestId);
        Assert.Equal(CallbackUrl, second.LinkRequests.Single().CallbackUrl);
        Assert.Equal(first.LinkRequests.Single().BankTransactionId, second.LinkRequests.Single().BankTransactionId);
        Assert.Equal(first.LinkRequests.Single().Amount, second.LinkRequests.Single().Amount);
    }

    [Fact]
    public async Task Transport_failure_tries_fallback()
    {
        var first = new TestProvider("first") { Create = (_, _) => throw new HttpRequestException("offline") };
        using var services = CreateServices([first, new TestProvider("second")]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("second", result.Value!.Attempt.ProviderName);
    }

    [Fact]
    public async Task Timeout_bounds_an_uncooperative_provider_and_ignores_its_late_result()
    {
        var clock = new ManualGatewayTimeProvider();
        var late = new TaskCompletionSource<Result<GatewayLink>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TestProvider("first") { Create = (_, _) => late.Task };
        using var services = CreateServices([first, new TestProvider("second")], clock, timeout: "00:00:07");
        var task = Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(task.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("second", result.Value!.Attempt.ProviderName);
        Assert.True(first.LastToken.IsCancellationRequested);
        late.SetResult(Result.Success(TestProvider.Link("first")));
        Assert.Equal("second-token", result.Value.Attempt.Token);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_start_another_bank()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var first = new TestProvider("first") { Create = (_, ct) => WaitForCancellation(ct) };
        var second = new TestProvider("second");
        using var services = CreateServices([first, second]);
        var task = Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: caller.Token);
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(second.LinkRequests);
    }

    [Fact]
    public async Task Missing_default_registration_uses_configured_fallback_only()
    {
        var unrelated = new TestProvider("unlisted");
        using var services = CreateServices([unrelated, new TestProvider("second")]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("second", result.Value!.Attempt.ProviderName);
        Assert.Empty(unrelated.LinkRequests);
    }

    [Fact]
    public async Task Exhausted_providers_return_failure_without_retrying_them()
    {
        var first = new TestProvider("first") { Create = (_, _) => Task.FromResult(Result.Failure<GatewayLink>(new Error("OFFLINE", "Offline"))) };
        var second = new TestProvider("second") { Create = first.Create };
        using var services = CreateServices([first, second]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAYS_EXHAUSTED", result.Error!.Code);
        Assert.Single(first.LinkRequests);
        Assert.Single(second.LinkRequests);
    }

    [Fact]
    public async Task Reverse_uses_the_retained_provider_even_when_it_fails()
    {
        var first = new TestProvider("first");
        var original = new TestProvider("second")
        {
            Reverse = (_, _) => Task.FromResult(Result.Failure<GatewayReversalResult>(new Error("UNKNOWN", "Unknown result")))
        };
        using var services = CreateServices([first, original]);
        var result = await Service(services).ReverseAsync(Paid("second"), TestContext.Current.CancellationToken);
        Assert.Equal("UNKNOWN", result.Error!.Code);
        Assert.Equal("Reverse", original.Operations.Single());
        Assert.Empty(first.Operations);
        Assert.Empty(first.LinkRequests);
    }

    [Fact]
    public async Task Inquiry_uses_the_retained_provider_even_when_it_fails()
    {
        var first = new TestProvider("first");
        var original = new TestProvider("second")
        {
            Inquiry = (_, _) => Task.FromResult(Result.Failure<GatewayInquiryObservation>(new Error("UNKNOWN", "Unknown result")))
        };
        using var services = CreateServices([first, original]);
        var result = await Service(services).InquiryAsync(Paid("second"), TestContext.Current.CancellationToken);
        Assert.Equal("UNKNOWN", result.Error!.Code);
        Assert.Equal("Inquiry", original.Operations.Single());
        Assert.Empty(first.Operations);
        Assert.Empty(first.LinkRequests);
    }

    [Fact]
    public async Task Missing_capability_is_unsupported_without_calling_another_provider()
    {
        var linkOnly = new LinkOnlyProvider("first");
        var capable = new TestProvider("second");
        using var services = CreateServices([capable], extra: linkOnly);
        var reversal = await Service(services).ReverseAsync(Paid("first"), TestContext.Current.CancellationToken);
        var inquiry = await Service(services).InquiryAsync(Paid("first"), TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_OPERATION_UNSUPPORTED", reversal.Error!.Code);
        Assert.Equal("PAYMENT_OPERATION_UNSUPPORTED", inquiry.Error!.Code);
        Assert.Empty(capable.Operations);
    }

    [Fact]
    public async Task Provider_receives_a_context_built_from_the_transaction_and_its_attempt()
    {
        GatewayPaymentContext? reversed = null, called = null;
        var provider = new TestProvider("first")
        {
            Reverse = (payment, _) => { reversed = payment; return Task.FromResult(Result.Success(new GatewayReversalResult(GatewayReversalState.Reversed))); },
            Callback = (request, _) => { called = request.Payment; return Task.FromResult(Result.Success(new GatewayCallbackResult(1001, "first-token", "receipt", GatewayPaymentState.Confirmed))); }
        };
        using var services = CreateServices([provider]);
        var transaction = Paid("first", id: 77, amount: 350);
        await Service(services).ReverseAsync(transaction, TestContext.Current.CancellationToken);
        await Service(services).VerifyCallbackAsync(transaction, NoFields, TestContext.Current.CancellationToken);

        var expected = new GatewayPaymentContext(77, Money.Create(350, "IRR"), transaction.GatewayAttempt!);
        Assert.Equal(expected, reversed);
        Assert.Equal(expected, called);
    }

    [Fact]
    public async Task Callback_fields_cannot_choose_the_provider()
    {
        var first = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("Callback fields chose a provider") };
        var retained = new TestProvider("second");
        using var services = CreateServices([first, retained]);
        var fields = new Dictionary<string, string> { ["provider"] = "first", ["ProviderName"] = "first", ["Token"] = "first-token" };
        var result = await Service(services).VerifyCallbackAsync(Paid("second"), fields, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal("second", result.Value!.Attempt.ProviderName);
        Assert.Equal("first", Assert.Single(retained.CallbackRequests).Fields["provider"]);
    }

    [Fact]
    public async Task Confirmed_callback_returns_the_attempt_to_retain_marked_confirmed()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Success(new GatewayCallbackResult(1001, "first-token", "456", GatewayPaymentState.Confirmed, "0"))) };
        using var services = CreateServices([provider]);
        var result = await Service(services).VerifyCallbackAsync(Paid(reference: null), NoFields, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(GatewayPaymentState.Confirmed, result.Value!.State);
        Assert.False(result.Value.IsDuplicate);
        Assert.Equal("0", result.Value.ProviderCode);
        Assert.Equal(GatewayAttempt.Create("first", 1001, "first-token", RequestedAt).WithConfirmation("456"), result.Value.Attempt);
    }

    [Theory]
    [InlineData(GatewayPaymentState.Unknown)]
    [InlineData(GatewayPaymentState.Failed)]
    public async Task Unconfirmed_callback_retains_a_learned_reference_without_confirming(GatewayPaymentState state)
    {
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Success(new GatewayCallbackResult(1001, "first-token", "456", state))) };
        using var services = CreateServices([provider]);
        var result = await Service(services).VerifyCallbackAsync(Paid(reference: null), NoFields, TestContext.Current.CancellationToken);

        Assert.Equal(state, result.Value!.State);
        Assert.False(result.Value.Attempt.IsConfirmed);
        Assert.Equal("456", result.Value.Attempt.ProviderReference);
    }

    [Fact]
    public async Task Duplicate_callback_for_a_confirmed_attempt_never_contacts_the_bank()
    {
        var logs = new CapturingLoggerProvider();
        var provider = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("A confirmed attempt reached the bank again") };
        using var services = CreateServices([provider], logs: logs);
        var confirmed = GatewayAttempt.Create("first", 1001, "first-token", RequestedAt).WithConfirmation("456");

        var result = await Service(services).VerifyCallbackAsync(new Transaction(attempt: confirmed), NoFields, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PaymentCallbackResult(confirmed, GatewayPaymentState.Confirmed, null, IsDuplicate: true), result.Value);
        Assert.Empty(provider.CallbackRequests);
        Assert.Equal("Payment callback from first for bank transaction 42 is a duplicate of a confirmed attempt; the bank was not contacted",
            Assert.Single(logs.From(ServiceCategory)).Message);
    }

    [Fact]
    public async Task Confirmed_outcome_without_a_bank_reference_is_rejected()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Success(new GatewayCallbackResult(1001, "first-token", null, GatewayPaymentState.Confirmed))) };
        using var services = CreateServices([provider]);
        var result = await Service(services).VerifyCallbackAsync(Paid(reference: null), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAY_RESPONSE_INVALID", result.Error!.Code);
    }

    [Theory]
    [InlineData(999, "first-token", "receipt")]
    [InlineData(1001, "other-token", "receipt")]
    [InlineData(1001, "first-token", "other-receipt")]
    public async Task Callback_reference_mismatch_is_rejected(long requestId, string token, string saleReference)
    {
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Success(new GatewayCallbackResult(requestId, token, saleReference, GatewayPaymentState.Confirmed))) };
        using var services = CreateServices([provider]);
        var result = await Service(services).VerifyCallbackAsync(Paid(), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_CALLBACK_INVALID", result.Error!.Code);
        Assert.Empty(provider.Operations);
    }

    [Fact]
    public async Task Outcome_without_a_reference_does_not_conflict_with_a_retained_one()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Success(new GatewayCallbackResult(1001, "first-token", null, GatewayPaymentState.Failed, "-138"))) };
        using var services = CreateServices([provider]);
        var result = await Service(services).VerifyCallbackAsync(Paid(), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal(GatewayPaymentState.Failed, result.Value!.State);
        Assert.Equal("receipt", result.Value.Attempt.ProviderReference);
    }

    [Theory]
    [InlineData(GatewayPaymentState.Confirmed, "0", LogLevel.Information)]
    [InlineData(GatewayPaymentState.Failed, "-138", LogLevel.Information)]
    [InlineData(GatewayPaymentState.Unknown, "-999", LogLevel.Warning)]
    public async Task Processed_callback_is_logged_with_its_outcome_but_no_token_or_reference(GatewayPaymentState state, string code, LogLevel level)
    {
        var logs = new CapturingLoggerProvider();
        var provider = new TestProvider("first") { Callback = (r, _) => Task.FromResult(Result.Success(new GatewayCallbackResult(r.Payment.RequestId, r.Payment.Token, "receipt", state, code))) };
        using var services = CreateServices([provider], logs: logs);
        var result = await Service(services).VerifyCallbackAsync(Paid(), NoFields, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var entry = Assert.Single(logs.From(ServiceCategory));
        Assert.Equal(level, entry.Level);
        // Exact match also proves the token ("first-token") and reference ("receipt") are absent.
        Assert.Equal($"Payment callback from first for bank transaction 42 processed: {state} (provider code {code})", entry.Message);
    }

    [Theory]
    [InlineData("PAYMENT_CALLBACK_INVALID", true)]
    [InlineData("PAYMENT_CONTEXT_INVALID", true)]
    [InlineData("PAYMENT_GATEWAY_TIMEOUT", false)]
    public async Task Failed_callback_checks_are_logged_as_suspicious_and_operational_failures_are_not(string code, bool suspicious)
    {
        var logs = new CapturingLoggerProvider();
        var error = code switch
        {
            "PAYMENT_CALLBACK_INVALID" => PaymentGatewayErrors.InvalidCallback,
            "PAYMENT_CONTEXT_INVALID" => PaymentGatewayErrors.InvalidContext,
            _ => PaymentGatewayErrors.Timeout
        };
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Failure<GatewayCallbackResult>(error)) };
        using var services = CreateServices([provider], logs: logs);

        var result = await Service(services).VerifyCallbackAsync(Paid(), NoFields, TestContext.Current.CancellationToken);

        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(logs.From(ServiceCategory));
        var rejection = Assert.Single(logs.Entries, e => e.Message.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, rejection.Level);
        Assert.Equal(suspicious, rejection.EventId.Name == "PaymentCallbackSuspicious");
        Assert.Contains("bank transaction 42", rejection.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 120, "PAYMENT_TRANSACTION_ID_INVALID")]
    [InlineData(-1, 120, "PAYMENT_TRANSACTION_ID_INVALID")]
    [InlineData(42, 0, "PAYMENT_AMOUNT_INVALID")]
    public async Task Invalid_payment_is_rejected_before_contacting_a_bank(long id, decimal amount, string code)
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        var result = await Service(services).CreateLinkAsync(new Transaction(id, amount), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(provider.LinkRequests);
    }

    [Fact]
    public async Task Missing_amount_is_rejected_before_contacting_a_bank()
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        var result = await Service(services).CreateLinkAsync(new Transaction(amount: null), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_AMOUNT_REQUIRED", result.Error!.Code);
        Assert.Empty(provider.LinkRequests);
    }

    [Fact]
    public async Task Empty_configuration_does_not_choose_an_arbitrary_registered_provider()
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider], defaultProvider: "", fallbacks: []);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAY_NOT_CONFIGURED", result.Error!.Code);
        Assert.Empty(provider.LinkRequests);
    }

    [Fact]
    public async Task Callback_failure_is_propagated_without_trying_another_provider()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => Task.FromResult(Result.Failure<GatewayCallbackResult>(new Error("INVALID_SIGNATURE", "Invalid signature"))) };
        var second = new TestProvider("second") { Callback = (_, _) => throw new InvalidOperationException("Callback must stay with its original provider") };
        using var services = CreateServices([provider, second]);
        var result = await Service(services).VerifyCallbackAsync(Paid(), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal("INVALID_SIGNATURE", result.Error!.Code);
        Assert.Empty(second.CallbackRequests);
    }

    [Fact]
    public async Task Missing_retained_provider_cannot_reverse_with_a_fallback()
    {
        var provider = new TestProvider("second");
        using var services = CreateServices([provider]);
        var result = await Service(services).ReverseAsync(Paid("first"), TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_PROVIDER_NOT_REGISTERED", result.Error!.Code);
        Assert.Empty(provider.Operations);
    }

    [Fact]
    public async Task Inquiry_compares_bank_amount_in_the_application_service()
    {
        var provider = new TestProvider("first")
        {
            Inquiry = (_, _) => Task.FromResult(Result.Success(
                new GatewayInquiryObservation(Money.Create(120, "IRR"), "receipt")))
        };
        using var services = CreateServices([provider]);

        var result = await Service(services).InquiryAsync(Paid(amount: 120), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.True(result.Value!.AmountMatchesExpected);
        Assert.Equal(Money.Create(120, "IRR"), result.Value.BankRecord.BankAmount);
    }

    [Fact]
    public async Task Inquiry_preserves_a_mismatched_bank_record()
    {
        var provider = new TestProvider("first")
        {
            Inquiry = (_, _) => Task.FromResult(Result.Success(
                new GatewayInquiryObservation(Money.Create(121, "IRR"), "receipt", ProviderCode: "0")))
        };
        using var services = CreateServices([provider]);

        var result = await Service(services).InquiryAsync(Paid(amount: 120), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.False(result.Value!.AmountMatchesExpected);
        Assert.Equal(121, result.Value.BankRecord.BankAmount.Amount);
        Assert.Equal("0", result.Value.BankRecord.ProviderCode);
    }

    [Fact]
    public async Task Reverse_returns_gateway_reversal_semantics()
    {
        var provider = new TestProvider("first")
        {
            Reverse = (_, _) => Task.FromResult(Result.Success(
                new GatewayReversalResult(GatewayReversalState.StillConfirmed, "receipt", "bank-code")))
        };
        using var services = CreateServices([provider]);
        var result = await Service(services).ReverseAsync(Paid(), TestContext.Current.CancellationToken);
        Assert.Equal(GatewayReversalState.StillConfirmed, result.Value!.State);
        Assert.Equal("bank-code", result.Value.ProviderCode);
        Assert.Single(provider.Operations);
    }

    [Fact]
    public async Task Inquiry_accepts_confirmed_and_unknown_attempts_with_a_reference()
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        var unknown = Paid();
        var confirmed = new Transaction(43, 120, unknown.GatewayAttempt!.WithConfirmation("receipt"));

        var unknownResult = await Service(services).InquiryAsync(unknown, TestContext.Current.CancellationToken);
        var confirmedResult = await Service(services).InquiryAsync(confirmed, TestContext.Current.CancellationToken);

        Assert.True(unknownResult.IsSuccess, unknownResult.Error?.Code);
        Assert.True(confirmedResult.IsSuccess, confirmedResult.Error?.Code);
        Assert.Equal(2, provider.Operations.Count(operation => operation == "Inquiry"));
    }

    [Fact]
    public async Task Inquiry_does_not_mutate_the_retained_attempt()
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        var transaction = Paid();
        var retained = transaction.GatewayAttempt;

        await Service(services).InquiryAsync(transaction, TestContext.Current.CancellationToken);

        Assert.Same(retained, transaction.GatewayAttempt);
        Assert.False(transaction.GatewayAttempt!.IsConfirmed);
    }

    [Fact]
    public async Task Inquiry_rejects_a_missing_reference_before_provider_dispatch()
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);

        var result = await Service(services).InquiryAsync(Paid(reference: null), TestContext.Current.CancellationToken);

        Assert.Equal("PAYMENT_CONTEXT_INVALID", result.Error!.Code);
        Assert.Empty(provider.Operations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Inquiry_rejects_an_invalid_provider_observation(int defect)
    {
        var valid = new GatewayInquiryObservation(Money.Create(120, "IRR"), "receipt");
        var observation = defect switch
        {
            0 => null!,
            1 => valid with { BankAmount = null! },
            2 => valid with { BankAmount = Money.Zero("IRR") },
            3 => valid with { ProviderReference = " " },
            _ => valid with { ProviderReference = "different-receipt" }
        };
        var provider = new TestProvider("first")
        {
            Inquiry = (_, _) => Task.FromResult(Result.Success<GatewayInquiryObservation>(observation))
        };
        using var services = CreateServices([provider]);

        var result = await Service(services).InquiryAsync(Paid(), TestContext.Current.CancellationToken);

        Assert.Equal("PAYMENT_GATEWAY_RESPONSE_INVALID", result.Error!.Code);
        Assert.Equal("Inquiry", Assert.Single(provider.Operations));
    }

    [Fact]
    public async Task Inquiry_log_contains_its_outcome_but_no_token_or_reference()
    {
        var logs = new CapturingLoggerProvider();
        var provider = new TestProvider("first");
        using var services = CreateServices([provider], logs: logs);

        await Service(services).InquiryAsync(Paid(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(logs.Entries, e => e.Message.Contains("operation Inquiry", StringComparison.Ordinal));
        Assert.Contains("Succeeded", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("first-token", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("receipt", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_timeout_during_reversal_leaves_outcome_unknown_and_never_fails_over()
    {
        var provider = new TestProvider("first") { Reverse = (_, _) => throw new TimeoutException() };
        var second = new TestProvider("second");
        using var services = CreateServices([provider, second]);
        var result = await Service(services).ReverseAsync(Paid(), TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAY_TIMEOUT", result.Error!.Code);
        Assert.Null(result.Value);
        Assert.Empty(second.Operations);
        Assert.Empty(second.LinkRequests);
    }

    [Fact]
    public async Task Duplicate_configured_names_are_attempted_once_in_priority_order()
    {
        var order = new List<string>();
        Task<Result<GatewayLink>> Fail(string name) { order.Add(name); return Task.FromResult(Result.Failure<GatewayLink>(PaymentGatewayErrors.Unavailable)); }
        var first = new TestProvider("first") { Create = (_, _) => Fail("first") };
        var second = new TestProvider("second") { Create = (_, _) => Fail("second") };
        var third = new TestProvider("third") { Create = (_, _) => { order.Add("third"); return Task.FromResult(Result.Success(TestProvider.Link("third"))); } };
        using var services = CreateServices([third, second, first], defaultProvider: "FIRST", fallbacks: ["first", "second", "SECOND", "third"]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("third", result.Value!.Attempt.ProviderName);
        Assert.Equal(["first", "second", "third"], order);
    }

    [Fact]
    public void Ambiguous_registration_is_rejected_instead_of_resolving_arbitrarily()
    {
        using var services = CreateServices([new TestProvider("first"), new TestProvider("FIRST")]);
        var result = services.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("first");
        Assert.Equal("PAYMENT_PROVIDER_AMBIGUOUS", result.Error!.Code);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("60.00:00:00")]
    public async Task Invalid_timeout_configuration_is_rejected_before_bank_call(string timeout)
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider], timeout: timeout);
        await Assert.ThrowsAsync<OptionsValidationException>(() => Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken));
        Assert.Empty(provider.LinkRequests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Malformed_handoff_does_not_stop_fallback(int defect)
    {
        var valid = TestProvider.Link("first");
        var malformed = defect switch
        {
            0 => null!,
            1 => valid with { Token = "" },
            2 => valid with { Redirect = null! },
            3 => valid with { Redirect = valid.Redirect with { Url = new Uri("file:///unsafe") } },
            4 => valid with { Redirect = valid.Redirect with { Method = (GatewayRedirectMethod)99 } },
            _ => valid with { Redirect = valid.Redirect with { Fields = null! } }
        };
        var provider = new TestProvider("first") { Create = (_, _) => Task.FromResult(Result.Success<GatewayLink>(malformed)) };
        using var services = CreateServices([provider, new TestProvider("second")]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: TestContext.Current.CancellationToken);
        Assert.Equal("second", result.Value!.Attempt.ProviderName);
    }

    [Theory]
    [InlineData("/relative")]
    [InlineData("file:///callback")]
    public async Task Invalid_callback_url_is_rejected_before_bank_call(string address)
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        var result = await Service(services).CreateLinkAsync(new Transaction(), new Uri(address, UriKind.RelativeOrAbsolute), ct: TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_CALLBACK_URL_INVALID", result.Error!.Code);
        Assert.Empty(provider.LinkRequests);
    }

    [Fact]
    public async Task Cancelled_caller_does_not_initiate_a_payment()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: cancelled.Token));
        Assert.Empty(provider.LinkRequests);
    }

    [Fact]
    public async Task Inquiry_rejects_a_missing_attempt_before_gateway_dispatch()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("No attempt reached bank ACL") };
        using var services = CreateServices([provider]);
        var inquiry = await Service(services).InquiryAsync(new Transaction(), TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAY_ATTEMPT_REQUIRED", inquiry.Error!.Code);
        Assert.Empty(provider.Operations);
    }

    [Fact]
    public async Task Reverse_and_callback_require_a_retained_attempt()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("No attempt reached bank ACL") };
        using var services = CreateServices([provider]);
        var reversal = await Service(services).ReverseAsync(new Transaction(), TestContext.Current.CancellationToken);
        var callback = await Service(services).VerifyCallbackAsync(new Transaction(), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAY_ATTEMPT_REQUIRED", reversal.Error!.Code);
        Assert.Equal("PAYMENT_GATEWAY_ATTEMPT_REQUIRED", callback.Error!.Code);
        Assert.Empty(provider.Operations);
    }

    [Fact]
    public async Task Operations_require_a_positive_transaction_id()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("Invalid transaction reached bank ACL") };
        using var services = CreateServices([provider]);
        var reversal = await Service(services).ReverseAsync(Paid(id: 0), TestContext.Current.CancellationToken);
        var inquiry = await Service(services).InquiryAsync(Paid(id: 0), TestContext.Current.CancellationToken);
        var callback = await Service(services).VerifyCallbackAsync(Paid(id: 0), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_TRANSACTION_ID_INVALID", reversal.Error!.Code);
        Assert.Equal("PAYMENT_TRANSACTION_ID_INVALID", inquiry.Error!.Code);
        Assert.Equal("PAYMENT_TRANSACTION_ID_INVALID", callback.Error!.Code);
        Assert.Empty(provider.Operations);
    }

    [Fact]
    public async Task Follow_up_and_callback_do_not_require_a_positive_amount()
    {
        var provider = new TestProvider("first");
        using var services = CreateServices([provider]);
        var reversal = await Service(services).ReverseAsync(Paid(amount: 0), TestContext.Current.CancellationToken);
        var callback = await Service(services).VerifyCallbackAsync(Paid(amount: 0), NoFields, TestContext.Current.CancellationToken);
        Assert.True(reversal.IsSuccess, reversal.Error?.Code);
        Assert.True(callback.IsSuccess, callback.Error?.Code);
    }

    [Fact]
    public async Task Callback_requires_its_fields()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("Missing fields reached bank ACL") };
        using var services = CreateServices([provider]);
        var result = await Service(services).VerifyCallbackAsync(Paid(), null!, TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_CALLBACK_FIELDS_REQUIRED", result.Error!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_callback_or_operation_outcome_is_not_accepted(bool missing)
    {
        var provider = new TestProvider("first")
        {
            Callback = (_, _) => Task.FromResult(Result.Success<GatewayCallbackResult>(missing ? null! : new(1001, "first-token", "receipt", (GatewayPaymentState)99))),
            Reverse = (_, _) => Task.FromResult(Result.Success<GatewayReversalResult>(missing ? null! : new((GatewayReversalState)99)))
        };
        using var services = CreateServices([provider]);
        var operation = await Service(services).ReverseAsync(Paid(), TestContext.Current.CancellationToken);
        var callback = await Service(services).VerifyCallbackAsync(Paid(), NoFields, TestContext.Current.CancellationToken);
        Assert.Equal("PAYMENT_GATEWAY_RESPONSE_INVALID", operation.Error!.Code);
        Assert.Equal("PAYMENT_GATEWAY_RESPONSE_INVALID", callback.Error!.Code);
    }

    [Fact]
    public async Task Caller_cancellation_wins_over_a_provider_completing_successfully()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new TestProvider("first") { Create = (_, _) => { caller.Cancel(); return Task.FromResult(Result.Success(TestProvider.Link("first"))); } };
        var second = new TestProvider("second");
        using var services = CreateServices([provider, second]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(services).CreateLinkAsync(new Transaction(), CallbackUrl, ct: caller.Token));
        Assert.Empty(second.LinkRequests);
    }

    [Fact]
    public async Task All_payment_operations_require_a_preexisting_bank_transaction()
    {
        var provider = new TestProvider("first") { Callback = (_, _) => throw new InvalidOperationException("Missing transaction reached bank ACL") };
        using var services = CreateServices([provider]);
        var service = Service(services);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("PAYMENT_TRANSACTION_REQUIRED", (await service.CreateLinkAsync(null!, CallbackUrl, ct: ct)).Error!.Code);
        Assert.Equal("PAYMENT_TRANSACTION_REQUIRED", (await service.VerifyCallbackAsync(null!, NoFields, ct)).Error!.Code);
        Assert.Equal("PAYMENT_TRANSACTION_REQUIRED", (await service.ReverseAsync(null!, ct)).Error!.Code);
        Assert.Equal("PAYMENT_TRANSACTION_REQUIRED", (await service.InquiryAsync(null!, ct)).Error!.Code);
        Assert.Empty(provider.LinkRequests);
        Assert.Empty(provider.Operations);
    }

    private static IPaymentGatewayService Service(ServiceProvider services) => services.GetRequiredService<IPaymentGatewayService>();

    private static Transaction Paid(string provider = "first", long id = 42, decimal amount = 120, string? reference = "receipt") =>
        new(id, amount, GatewayAttempt.Create(provider, 1001, provider + "-token", RequestedAt, reference));

    private static ServiceProvider CreateServices(TestProvider[] providers, TimeProvider? clock = null,
        string defaultProvider = "first", string[]? fallbacks = null, string timeout = "00:00:10",
        CapturingLoggerProvider? logs = null, IPaymentGatewayProvider? extra = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Payment:Gateways:DefaultProvider"] = defaultProvider,
            ["Payment:Gateways:LinkCreationTimeout"] = timeout
        };
        var names = fallbacks ?? ["second"];
        for (var i = 0; i < names.Length; i++) config[$"Payment:Gateways:FallbackProviders:{i}"] = names[i];
        var services = new ServiceCollection();
        if (logs is null) services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        else services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSingleton<IIdGenerator>(new SequentialIds());
        services.AddSingleton(clock ?? new ManualGatewayTimeProvider(RequestedAt));
        foreach (var provider in providers) services.AddSingleton<IPaymentGatewayProvider>(provider);
        if (extra is not null) services.AddSingleton(extra);
        services.AddPaymentModule(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        return services.BuildServiceProvider();
    }

    private static async Task<Result<GatewayLink>> WaitForCancellation(CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("Unreachable");
    }

    private sealed class Transaction(long id = 42, decimal? amount = 120, GatewayAttempt? attempt = null) : AggregateRoot<long>, IBankTransaction
    {
        public Money Amount { get; } = amount is { } value ? Money.Create(value, "IRR") : null!;
        public GatewayAttempt? GatewayAttempt { get; } = attempt;
        long IBankTransaction.Id => id;
    }

    private sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];
        public IEnumerable<LogEntry> From(string category) => Entries.Where(e => e.Category == category);
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);
        public void Dispose() { }

        private sealed class Logger(string category, List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add(new(category, logLevel, eventId, formatter(state, exception)));
        }
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private long _next = 1000;
        public long Generate() => Interlocked.Increment(ref _next);
    }

    private sealed class LinkOnlyProvider(string name) : IPaymentGatewayProvider
    {
        public string Name => name;
        public Task<Result<GatewayLink>> CreateLinkAsync(GatewayLinkRequest request, CancellationToken ct) => Task.FromResult(Result.Success(TestProvider.Link(name)));
        public Task<Result<GatewayCallbackResult>> VerifyCallbackAsync(GatewayCallbackRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TestProvider(string name) : IPaymentGatewayProvider, IPaymentReversalProvider, IPaymentInquiryProvider
    {
        public string Name => name;
        public List<GatewayLinkRequest> LinkRequests { get; } = [];
        public List<GatewayCallbackRequest> CallbackRequests { get; } = [];
        public List<string> Operations { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public Func<GatewayLinkRequest, CancellationToken, Task<Result<GatewayLink>>>? Create { get; init; }
        public Func<GatewayPaymentContext, CancellationToken, Task<Result<GatewayReversalResult>>>? Reverse { get; init; }
        public Func<GatewayPaymentContext, CancellationToken, Task<Result<GatewayInquiryObservation>>>? Inquiry { get; init; }
        public Func<GatewayCallbackRequest, CancellationToken, Task<Result<GatewayCallbackResult>>>? Callback { get; init; }

        public Task<Result<GatewayLink>> CreateLinkAsync(GatewayLinkRequest request, CancellationToken ct)
        {
            LinkRequests.Add(request);
            LastToken = ct;
            return Create?.Invoke(request, ct) ?? Task.FromResult(Result.Success(Link(name)));
        }

        public Task<Result<GatewayCallbackResult>> VerifyCallbackAsync(GatewayCallbackRequest request, CancellationToken ct)
        {
            CallbackRequests.Add(request);
            return Callback?.Invoke(request, ct) ?? Task.FromResult(Result.Success(new GatewayCallbackResult(request.Payment.RequestId, request.Payment.Token, "receipt", GatewayPaymentState.Confirmed, "0")));
        }

        public Task<Result<GatewayReversalResult>> ReverseAsync(GatewayPaymentContext payment, CancellationToken ct)
        {
            Operations.Add("Reverse");
            return Reverse?.Invoke(payment, ct)
                ?? Task.FromResult(Result.Success(new GatewayReversalResult(GatewayReversalState.Reversed)));
        }

        public Task<Result<GatewayInquiryObservation>> InquiryAsync(GatewayPaymentContext payment, CancellationToken ct)
        {
            Operations.Add("Inquiry");
            return Inquiry?.Invoke(payment, ct)
                ?? Task.FromResult(Result.Success(new GatewayInquiryObservation(
                    Money.Create(120, "IRR"), payment.ProviderReference ?? "receipt")));
        }

        public static GatewayLink Link(string name) => new(name + "-token",
            new(new Uri("https://bank.example/pay"), GatewayRedirectMethod.Post, new Dictionary<string, string> { ["RefId"] = name + "-token" }));
    }
}
