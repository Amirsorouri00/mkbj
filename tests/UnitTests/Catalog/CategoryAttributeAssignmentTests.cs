using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

public class CategoryAttributeAssignmentTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static long NextId() => IdGenerator.Generate();

    [Fact]
    public void Create_Allows_All_Flags_Independently_True_At_Once()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now,
            isRequired: true, isFilterable: true, isSearchable: true, isInTitle: true, titleOrder: 1);

        a.IsRequired.Should().BeTrue();
        a.IsFilterable.Should().BeTrue();
        a.IsSearchable.Should().BeTrue();
        a.IsInTitle.Should().BeTrue();
        a.TitleOrder.Should().Be(1);
        a.DomainEvents.Should().ContainSingle(e => e is CategoryAttributeAssignedDomainEvent);
    }

    [Fact]
    public void Create_Clears_TitleOrder_When_Not_InTitle()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isInTitle: false, titleOrder: 5);
        a.TitleOrder.Should().BeNull();
    }

    [Fact]
    public void Create_With_Primary_Forces_Required_On()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isPrimary: true);

        a.IsPrimary.Should().BeTrue();
        a.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Create_With_Both_Primary_And_VariantGenerating_Lets_VariantGenerating_Win()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now,
            isPrimary: true, isVariantGenerating: true);

        a.IsVariantGenerating.Should().BeTrue();
        a.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void SetRequired_False_To_True_Raises_BecameRequired_Event()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: false);
        a.ClearDomainEvents();

        a.SetRequired(true, TestTime.Now);

        a.IsRequired.Should().BeTrue();
        a.DomainEvents.Should().ContainSingle(e => e is CategoryAttributeBecameRequiredDomainEvent);
    }

    [Fact]
    public void SetRequired_True_To_False_Raises_FlagsChanged_Not_BecameRequired()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: true);
        a.ClearDomainEvents();

        a.SetRequired(false, TestTime.Now);

        a.IsRequired.Should().BeFalse();
        a.DomainEvents.Should().ContainSingle(e => e is CategoryAttributeFlagsChangedDomainEvent);
        a.DomainEvents.Should().NotContain(e => e is CategoryAttributeBecameRequiredDomainEvent);
    }

    [Fact]
    public void SetRequired_To_Same_Value_Is_A_NoOp()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: true);
        a.ClearDomainEvents();

        a.SetRequired(true, TestTime.Now);

        a.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetRequired_False_Throws_While_Primary_Is_Enabled()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isPrimary: true);
        var act = () => a.SetRequired(false, TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "CATEGORY_ATTRIBUTE_REQUIRED_LOCKED_BY_PRIMARY");
    }

    [Fact]
    public void SetFilterable_And_SetSearchable_Toggle_Independently_Of_Other_Flags()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: true, isInTitle: true, titleOrder: 0);
        a.ClearDomainEvents();

        a.SetFilterable(true, TestTime.Now);
        a.SetSearchable(true, TestTime.Now);

        a.IsFilterable.Should().BeTrue();
        a.IsSearchable.Should().BeTrue();
        a.IsRequired.Should().BeTrue();
        a.IsInTitle.Should().BeTrue();
        a.DomainEvents.Should().HaveCount(2).And.AllSatisfy(e => e.Should().BeOfType<CategoryAttributeFlagsChangedDomainEvent>());
    }

    [Fact]
    public void SetTitleConfiguration_Enabling_InTitle_Sets_Order_And_Raises_Event()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isInTitle: false);
        a.ClearDomainEvents();

        a.SetTitleConfiguration(true, 2, TestTime.Now);

        a.IsInTitle.Should().BeTrue();
        a.TitleOrder.Should().Be(2);
        a.DomainEvents.Should().ContainSingle(e => e is CategoryAttributeTitleConfigurationChangedDomainEvent);
    }

    [Fact]
    public void SetTitleConfiguration_Disabling_InTitle_Clears_Order()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isInTitle: true, titleOrder: 3);
        a.ClearDomainEvents();

        a.SetTitleConfiguration(false, null, TestTime.Now);

        a.IsInTitle.Should().BeFalse();
        a.TitleOrder.Should().BeNull();
    }

    [Fact]
    public void Reorder_Requires_InTitle()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isInTitle: false);
        var act = () => a.Reorder(1, TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "CATEGORY_ATTRIBUTE_NOT_IN_TITLE");
    }

    [Fact]
    public void Reorder_Rejects_Negative_Order()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isInTitle: true, titleOrder: 0);
        var act = () => a.Reorder(-1, TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "CATEGORY_ATTRIBUTE_TITLE_ORDER_INVALID");
    }

    [Fact]
    public void SetPrimary_Enabling_Forces_Required_On_And_Raises_Events()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: false);
        a.ClearDomainEvents();

        a.SetPrimary(true, TestTime.Now);

        a.IsPrimary.Should().BeTrue();
        a.IsRequired.Should().BeTrue();
        a.DomainEvents.Should().Contain(e => e is CategoryAttributePriorityChangedDomainEvent);
        a.DomainEvents.Should().Contain(e => e is CategoryAttributeBecameRequiredDomainEvent);
    }

    [Fact]
    public void SetPrimary_Disabling_Does_Not_Clear_Required()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: true, isPrimary: true);
        a.ClearDomainEvents();

        a.SetPrimary(false, TestTime.Now);

        a.IsPrimary.Should().BeFalse();
        a.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void SetPrimary_Throws_When_VariantGenerating_Is_Enabled()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isVariantGenerating: true);
        var act = () => a.SetPrimary(true, TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "CATEGORY_ATTRIBUTE_PRIMARY_BLOCKED_BY_VARIANT_GENERATING");
    }

    [Fact]
    public void SetVariantGenerating_Enabling_Silently_Disables_Primary()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isPrimary: true);
        a.ClearDomainEvents();

        a.SetVariantGenerating(true, TestTime.Now);

        a.IsVariantGenerating.Should().BeTrue();
        a.IsPrimary.Should().BeFalse();
        a.DomainEvents.Should().ContainSingle(e => e is CategoryAttributePriorityChangedDomainEvent);
    }

    [Fact]
    public void SetVariantGenerating_Disabling_Does_Not_Restore_Primary()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isVariantGenerating: true);
        a.ClearDomainEvents();

        a.SetVariantGenerating(false, TestTime.Now);

        a.IsVariantGenerating.Should().BeFalse();
        a.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void Remove_Is_Idempotent_And_Raises_Event_Once()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now);
        a.ClearDomainEvents();

        a.Remove(TestTime.Now);
        a.Remove(TestTime.Now);

        a.RemovedAt.Should().NotBeNull();
        a.DomainEvents.Should().ContainSingle(e => e is CategoryAttributeUnassignedDomainEvent);
    }

    [Fact]
    public void Mutating_A_Removed_Assignment_Throws()
    {
        var a = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now);
        a.Remove(TestTime.Now);

        var act = () => a.SetRequired(true, TestTime.Now);
        act.Should().Throw<DomainException>().Where(e => e.Code == "CATEGORY_ATTRIBUTE_ASSIGNMENT_REMOVED");
    }
}
