using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Outcomes;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Money;

/// <summary>
/// What makes two partner order requests "the same request".
/// </summary>
/// <remarks>
/// A partner that retries after a timeout must not be billed twice, so the fingerprint is what a
/// repeat is recognised by. It covers the customer, the amount and the currency — the three
/// things that decide what is sold and for how much — and deliberately nothing else: a retry that
/// also corrects a typo in the buyer's phone number is still the same order.
/// </remarks>
public sealed class RequestFingerprintTests
{
    /// <summary>
    /// A lowercase hex SHA-256, stable across runs.
    /// </summary>
    /// <remarks>
    /// The exact bytes hashed are not asserted. The plan specified
    /// <c>SHA256("{ext}|{minor}|{CUR}")</c>, but a customer id is partner-supplied free text and
    /// may contain a pipe — see
    /// <see cref="Fields_Containing_The_Separator_Cannot_Impersonate_Another_Request"/>, which
    /// that form fails. The fingerprint is never exposed to a partner, so its encoding is not a
    /// contract; its properties are, and they are what these tests hold.
    /// </remarks>
    [Fact]
    public void Fingerprint_Is_Lowercase_Hex_Sha256_And_Stable()
    {
        var fingerprint = RequestFingerprint.Compute("C-77", 50000, "USD");

        fingerprint.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        fingerprint.Should().Be(RequestFingerprint.Compute("C-77", 50000, "USD"));
    }

    [Fact]
    public void Fingerprint_Changes_With_Amount() =>
        RequestFingerprint.Compute("C-77", 50001, "USD")
            .Should().NotBe(RequestFingerprint.Compute("C-77", 50000, "USD"));

    [Fact]
    public void Fingerprint_Changes_With_Currency() =>
        RequestFingerprint.Compute("C-77", 50000, "SGD")
            .Should().NotBe(RequestFingerprint.Compute("C-77", 50000, "USD"));

    [Fact]
    public void Fingerprint_Changes_With_Customer() =>
        RequestFingerprint.Compute("C-78", 50000, "USD")
            .Should().NotBe(RequestFingerprint.Compute("C-77", 50000, "USD"));

    [Fact]
    public void Currency_Case_Does_Not_Make_A_Different_Request() =>
        // A retry that sends "usd" where the first attempt sent "USD" is the same order, and
        // billing it twice because of letter case would be indefensible.
        RequestFingerprint.Compute("C-77", 50000, "usd")
            .Should().Be(RequestFingerprint.Compute("C-77", 50000, "USD"));

    /// <summary>
    /// Two different requests whose fields contain the separator must not hash alike.
    /// </summary>
    /// <remarks>
    /// Joined plainly, both of these are the bytes <c>C|50000|1|USD</c>: the pipe moves from
    /// inside the customer id to inside the currency, and the three fields re-divide. One
    /// partner's retry would then be answered with another partner's order. The fields are
    /// length-prefixed so the division cannot move.
    /// </remarks>
    [Fact]
    public void Fields_Containing_The_Separator_Cannot_Impersonate_Another_Request() =>
        RequestFingerprint.Compute("C|50000", 1, "USD")
            .Should().NotBe(RequestFingerprint.Compute("C", 50000, "1|USD"));
}

/// <summary>
/// The error catalogue, checked for the one property a caller automates against.
/// </summary>
public sealed class SalesErrorsTests
{
    [Fact]
    public void Retryable_Errors_Are_Exactly_Timeout_And_Batch_Deadline() =>
        // Retryable is a promise to the caller that sending the same request again is safe and
        // may work. Every other failure here is a decision that will not change on a retry, and
        // marking one retryable would invite a partner to hammer it.
        typeof(SalesErrors).GetFields().Select(field => field.GetValue(null)).OfType<Error>()
            .Where(error => error.Retryable).Select(error => error.Code)
            .Should().BeEquivalentTo(["COMPOSITION_TIMEOUT", "BATCH_DEADLINE_EXCEEDED"]);

    [Fact]
    public void Every_Error_Carries_A_Code_And_A_Usable_Status() =>
        typeof(SalesErrors).GetFields().Select(field => field.GetValue(null)).OfType<Error>()
            .Should().OnlyContain(error =>
                !string.IsNullOrWhiteSpace(error.Code) && error.HttpStatus >= 400);
}
