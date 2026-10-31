using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.ValueObjects;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using Xunit;

using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Customers.Domain.Customer;

namespace EwaysVNext.UnitTests.Customers;

public sealed class CustomerDomainTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private long _nextId = 100;

    private Func<long> Ids() => () => _nextId++;

    private static Email? E(string? value) => value is null ? null : Email.Create(value);

    private static Phone? P(string? value)
    {
        if (value is null)
            return null;

        Phone.TryCreate(value, out var phone);
        return phone;
    }

    private static string CodeOf(Action act) =>
        FluentActions.Invoking(act).Should().Throw<DomainException>().Which.Code;

    private static CustomerAggregate PartnerLinked(string? email = "a@x.com", string? phone = "+6591234567", string? name = "Martin") =>
        CustomerAggregate.CreatePartnerLinked(1, 7, "456", E(email), P(phone), name, Now);

    private static CustomerAggregate Website(bool verified = false) =>
        CustomerAggregate.CreateWebsite(2, Guid.NewGuid(), Email.Create("w@x.com"), verified, Now);

    [Theory]
    [InlineData(null, null)]
    public void PartnerLinked_Requires_Email_Or_Phone(string? email, string? phone) =>
        CodeOf(() => CustomerAggregate.CreatePartnerLinked(1, 7, "456", E(email), P(phone), null, Now))
            .Should().Be("CUSTOMER_CONTACT_REQUIRED");

    [Theory]
    [InlineData(null, "+6591234567")]
    [InlineData("m@x.com", null)]
    public void One_Contact_Channel_Is_Enough(string? email, string? phone) =>
        FluentActions.Invoking(() =>
                CustomerAggregate.CreatePartnerLinked(1, 7, "456", E(email), P(phone), null, Now))
            .Should().NotThrow("an invoice needs somewhere to go, not every channel");

    [Fact]
    public void A_Partner_Assertion_Never_Counts_As_Verification() =>
        PartnerLinked().EmailVerified.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void The_Partner_Reference_Is_Required(string external) =>
        CodeOf(() => CustomerAggregate.CreatePartnerLinked(1, 7, external, E("a@x.com"), null, null, Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Sync_With_Omitted_Fields_Keeps_Stored_Values()
    {
        var customer = PartnerLinked();

        customer.SyncPartnerContact(new ContactUpdate(null, null, null), 7, "corr-1", Ids(), Now)
            .Should().BeFalse();

        (customer.Email, customer.Phone, customer.FullName)
            .Should().Be(("a@x.com", "+6591234567", "Martin"));

        customer.ContactChanges.Should().BeEmpty();
    }

    [Fact]
    public void Sync_Appends_One_Change_Per_Changed_Field_With_Source_And_Correlation()
    {
        var customer = PartnerLinked();

        var changed = customer.SyncPartnerContact(
            new ContactUpdate(E("b@x.com"), P("+6591234567"), "Martin Tan"), 7, "corr-2", Ids(), Now);

        changed.Should().BeTrue();

        customer.ContactChanges.Should().HaveCount(2, "the phone was re-sent unchanged");

        customer.ContactChanges.Should().ContainSingle(change =>
            change.Field == ContactField.Email
            && change.OldValue == "a@x.com"
            && change.NewValue == "b@x.com"
            && change.SourcePartnerId == 7
            && change.CorrelationId == "corr-2");

        customer.ContactChanges.Should().ContainSingle(change =>
            change.Field == ContactField.FullName
            && change.OldValue == "Martin"
            && change.NewValue == "Martin Tan");

        customer.EmailVerified.Should().BeFalse(
            "a partner changing the address does not prove the new one");
    }

    [Fact]
    public void Re_Syncing_The_Same_Values_Records_Nothing()
    {
        var customer = PartnerLinked();

        customer.SyncPartnerContact(
            new ContactUpdate(E("a@x.com"), P("+6591234567"), "Martin"), 7, "corr-3", Ids(), Now)
            .Should().BeFalse();

        customer.ContactChanges.Should().BeEmpty();
    }

    [Fact]
    public void Website_Mirror_Takes_Verification_From_Identity()
    {
        var customer = Website(verified: false);

        customer.MirrorIdentity(Email.Create("w@x.com"), emailVerified: true, "corr-4", Ids(), Now);

        customer.EmailVerified.Should().BeTrue();
        customer.ContactChanges.Should().BeEmpty(
            "confirming an address they already had is not an edit to record");
    }

    [Fact]
    public void Website_Mirror_Records_A_Changed_Address()
    {
        var customer = Website();

        customer.MirrorIdentity(Email.Create("new@x.com"), emailVerified: true, "corr-5", Ids(), Now);

        customer.Email.Should().Be("new@x.com");
        customer.ContactChanges.Should().ContainSingle(change =>
            change.Field == ContactField.Email && change.SourcePartnerId == null);
    }

    [Fact]
    public void Profile_Update_On_Suspended_Customer_Is_Refused()
    {
        var customer = Website();
        customer.Suspend();

        CodeOf(() => customer.UpdateProfile(
                new ProfileUpdate("Someone", null, null, null, null), "corr-6", Ids(), Now))
            .Should().Be("CUSTOMER_SUSPENDED");
    }

    [Fact]
    public void Suspension_Error_Maps_To_403() =>
        EwaysVNext.Outcomes.Error.SuggestHttpStatus("CUSTOMER_SUSPENDED").Should().Be(403);

    [Fact]
    public void Website_Only_Methods_Refuse_PartnerLinked()
    {
        var customer = PartnerLinked();

        CodeOf(() => customer.MirrorIdentity(Email.Create("a@x.com"), true, "c", Ids(), Now))
            .Should().Be("VALIDATION_FAILED");

        CodeOf(() => customer.UpdateProfile(new ProfileUpdate(null, null, null, null, null), "c", Ids(), Now))
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void Partner_Sync_Refuses_A_Website_Customer() =>
        CodeOf(() => Website().SyncPartnerContact(new ContactUpdate(null, null, null), 7, "c", Ids(), Now))
            .Should().Be("VALIDATION_FAILED");

    [Fact]
    public void Profile_Update_Leaves_Omitted_Fields_Alone()
    {
        var customer = Website();
        customer.UpdateProfile(
            new ProfileUpdate("Martin", "Acme", P("+6591234567"), null, "REG-1"), "corr-7", Ids(), Now);

        customer.UpdateProfile(
            new ProfileUpdate(null, null, null, null, null), "corr-8", Ids(), Now);

        customer.FullName.Should().Be("Martin");
        customer.CompanyName.Should().Be("Acme");
        customer.Phone.Should().Be("+6591234567");
        customer.RegistrationNumber.Should().Be("REG-1");
    }

    [Fact]
    public void Profile_Update_Records_Only_Contact_Fields()
    {
        var customer = Website();

        customer.UpdateProfile(
            new ProfileUpdate("Martin", "Acme", P("+6591234567"), null, "REG-1"), "corr-9", Ids(), Now);

        customer.ContactChanges.Select(change => change.Field)
            .Should().BeEquivalentTo([ContactField.FullName, ContactField.Phone],
                "a dispute turns on where we were told to send things, not the company name");
    }
}
