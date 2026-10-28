using System.Globalization;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintOptionDomainTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChangedAt = CreatedAt.AddMinutes(1);

    [Theory]
    [InlineData(AttributeBlueprintDataType.Text, " Cotton ", null, "Cotton")]
    [InlineData(AttributeBlueprintDataType.Text, "123", null, "123")]
    [InlineData(AttributeBlueprintDataType.Text, "قرمز", null, "قرمز")]
    [InlineData(AttributeBlueprintDataType.Number, " +005.5000 ", 1L, "5.5")]
    [InlineData(AttributeBlueprintDataType.Number, "-005.5000", 1L, "-5.5")]
    [InlineData(AttributeBlueprintDataType.Number, "-0.00", 1L, "0")]
    [InlineData(AttributeBlueprintDataType.Number, ".5", 1L, "0.5")]
    [InlineData(AttributeBlueprintDataType.Number, "5.", 1L, "5")]
    [InlineData(AttributeBlueprintDataType.Number, "0.0000000000000000000000000001", 1L, "0.0000000000000000000000000001")]
    [InlineData(AttributeBlueprintDataType.Number, "79228162514264337593543950335", 1L, "79228162514264337593543950335")]
    [InlineData(AttributeBlueprintDataType.Number, "-79228162514264337593543950335", 1L, "-79228162514264337593543950335")]
    [InlineData(AttributeBlueprintDataType.Date, " 2024-02-29 ", null, "2024-02-29")]
    public void Add_Normalizes_And_Raises_Event_On_Blueprint(AttributeBlueprintDataType type, string value, long? measurementUnitId, string expected)
    {
        var blueprint = Create(type);

        blueprint.AddOption(101, value, measurementUnitId, ChangedAt);

        var option = blueprint.Options.Should().ContainSingle().Subject;
        option.Id.Should().Be(101);
        option.AttributeBlueprintId.Should().Be(42);
        option.Value.Should().Be(expected);
        option.MeasurementUnitId.Should().Be(measurementUnitId);
        option.DomainEvents.Should().BeEmpty();
        blueprint.UpdatedAt.Should().Be(ChangedAt);
        var added = blueprint.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<AttributeBlueprintOptionAddedDomainEvent>().Subject;
        added.AttributeBlueprintId.Should().Be(42);
        added.OptionId.Should().Be(101);
        added.Value.Should().Be(expected);
        added.MeasurementUnitId.Should().Be(measurementUnitId);
    }

    [Theory]
    [InlineData(AttributeBlueprintDataType.Text, null, null, "VALUE_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Text, "", null, "VALUE_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Text, "  ", null, "VALUE_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Number, "  ", 1L, "VALUE_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Date, "  ", null, "VALUE_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Text, "Cotton", 1L, "UNIT_NOT_ALLOWED")]
    [InlineData(AttributeBlueprintDataType.Text, "Cotton", 0L, "UNIT_NOT_ALLOWED")]
    [InlineData(AttributeBlueprintDataType.Date, "2026-09-16", 1L, "UNIT_NOT_ALLOWED")]
    [InlineData(AttributeBlueprintDataType.Number, "5", null, "UNIT_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Number, "5", 0L, "UNIT_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Number, "5", -1L, "UNIT_REQUIRED")]
    [InlineData(AttributeBlueprintDataType.Number, "abc", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "1,5", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "1,000", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "1e3", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "NaN", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "Infinity", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "79228162514264337593543950336", 1L, "NUMBER_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "1.00000000000000000000000000001", 1L, "NUMBER_PRECISION_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "0.00000000000000000000000000001", 1L, "NUMBER_PRECISION_INVALID")]
    [InlineData(AttributeBlueprintDataType.Number, "-0.00000000000000000000000000001", 1L, "NUMBER_PRECISION_INVALID")]
    [InlineData(AttributeBlueprintDataType.Date, "2026-02-29", null, "DATE_INVALID")]
    [InlineData(AttributeBlueprintDataType.Date, "2026-13-01", null, "DATE_INVALID")]
    [InlineData(AttributeBlueprintDataType.Date, "01/02/2026", null, "DATE_INVALID")]
    [InlineData(AttributeBlueprintDataType.Date, "2026-9-1", null, "DATE_INVALID")]
    [InlineData(AttributeBlueprintDataType.Date, "2026-09-16T00:00:00Z", null, "DATE_INVALID")]
    public void Invalid_Add_And_Update_Leave_State_Unchanged(AttributeBlueprintDataType type, string? value, long? measurementUnitId, string code)
    {
        foreach (var update in new[] { false, true })
        {
            var blueprint = Create(type);
            var original = type switch
            {
                AttributeBlueprintDataType.Number => "1",
                AttributeBlueprintDataType.Date => "2026-09-15",
                _ => "Original"
            };
            var originalMeasurementUnitId = type == AttributeBlueprintDataType.Number ? 2L : (long?)null;
            blueprint.AddOption(101, original, originalMeasurementUnitId, CreatedAt);
            blueprint.ClearDomainEvents();

            Action act = update
                ? () => blueprint.UpdateOption(101, value!, measurementUnitId, ChangedAt)
                : () => blueprint.AddOption(102, value!, measurementUnitId, ChangedAt);

            act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_" + code);
            var option = blueprint.Options.Should().ContainSingle().Subject;
            option.Value.Should().Be(original);
            option.MeasurementUnitId.Should().Be(originalMeasurementUnitId);
            blueprint.UpdatedAt.Should().Be(CreatedAt);
            blueprint.DomainEvents.Should().BeEmpty();
        }
    }

    [Fact]
    public void Value_Length_Limit_Is_Checked_Before_Mutation()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        var value = new string('x', 1000);
        blueprint.AddOption(101, " " + value + " ", null, CreatedAt);
        blueprint.ClearDomainEvents();

        Action add = () => blueprint.AddOption(102, value + "x", null, ChangedAt);
        Action update = () => blueprint.UpdateOption(101, value + "x", null, ChangedAt);

        add.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TOO_LONG");
        update.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TOO_LONG");
        blueprint.Options.Should().ContainSingle().Subject.Value.Should().Be(value);
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("1")]
    public void Boolean_Blueprints_Reject_Options(string value)
    {
        var blueprint = Create(AttributeBlueprintDataType.Boolean);
        Action act = () => blueprint.AddOption(101, value, null, ChangedAt);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_BOOLEAN_OPTIONS_NOT_ALLOWED");
        blueprint.Options.Should().BeEmpty();
        blueprint.UpdatedAt.Should().BeNull();
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void All_Option_Operations_Reject_Invalid_Ids(long id)
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        Action add = () => blueprint.AddOption(id, "Cotton", null, ChangedAt);
        Action update = () => blueprint.UpdateOption(id, "Cotton", null, ChangedAt);
        Action remove = () => blueprint.RemoveOption(id, ChangedAt);

        foreach (var act in new[] { add, update, remove })
            act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_ID_INVALID");

        blueprint.Options.Should().BeEmpty();
        blueprint.UpdatedAt.Should().BeNull();
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_Id_Is_Rejected_Even_When_Value_Is_Different()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Cotton", null, CreatedAt);
        blueprint.ClearDomainEvents();
        Action act = () => blueprint.AddOption(101, "Wool", null, ChangedAt);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_ID_TAKEN");
        blueprint.Options.Should().ContainSingle().Subject.Value.Should().Be("Cotton");
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AttributeBlueprintDataType.Text, "Cotton", " cotton ", "Wool", null)]
    [InlineData(AttributeBlueprintDataType.Number, "5", "05.00", "6", 1L)]
    [InlineData(AttributeBlueprintDataType.Date, "2026-09-15", " 2026-09-15 ", "2026-09-16", null)]
    public void Duplicate_Value_Is_Rejected_On_Add_And_Update(AttributeBlueprintDataType type, string existing, string duplicate, string other, long? measurementUnitId)
    {
        var blueprint = Create(type);
        blueprint.AddOption(101, existing, measurementUnitId, CreatedAt);
        blueprint.AddOption(102, other, measurementUnitId, CreatedAt);
        blueprint.ClearDomainEvents();

        Action add = () => blueprint.AddOption(103, duplicate, measurementUnitId, ChangedAt);
        Action update = () => blueprint.UpdateOption(102, duplicate, measurementUnitId, ChangedAt);

        foreach (var act in new[] { add, update })
            act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TAKEN");

        blueprint.Options.Select(option => option.Value).Should().Equal(existing, other);
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Numeric_Uniqueness_Includes_Unit_And_Update_Preserves_Identity()
    {
        var blueprint = Create(AttributeBlueprintDataType.Number);
        blueprint.AddOption(101, "5", 1, CreatedAt);
        blueprint.AddOption(102, "5.0", 2, CreatedAt);
        blueprint.ClearDomainEvents();
        var option = blueprint.Options.Last();

        Action duplicate = () => blueprint.UpdateOption(102, "5", 1, ChangedAt);
        duplicate.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TAKEN");
        option.MeasurementUnitId.Should().Be(2);
        blueprint.DomainEvents.Should().BeEmpty();

        blueprint.UpdateOption(102, "6.00", 1, ChangedAt);

        blueprint.Options.Last().Should().BeSameAs(option);
        option.Id.Should().Be(102);
        option.AttributeBlueprintId.Should().Be(42);
        option.Value.Should().Be("6");
        option.MeasurementUnitId.Should().Be(1);
        blueprint.UpdatedAt.Should().Be(ChangedAt);
        var updated = blueprint.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<AttributeBlueprintOptionUpdatedDomainEvent>().Subject;
        updated.AttributeBlueprintId.Should().Be(42);
        updated.OptionId.Should().Be(102);
        updated.Value.Should().Be("6");
        updated.MeasurementUnitId.Should().Be(1);
    }

    [Theory]
    [InlineData(AttributeBlueprintDataType.Text, "Cotton", " Cotton ", null)]
    [InlineData(AttributeBlueprintDataType.Number, "5", "+005.00", 1L)]
    [InlineData(AttributeBlueprintDataType.Date, "2026-09-15", " 2026-09-15 ", null)]
    public void Normalized_NoOp_Update_Does_Not_Touch_Or_Raise_Events(AttributeBlueprintDataType type, string original, string updated, long? measurementUnitId)
    {
        var blueprint = Create(type);
        blueprint.AddOption(101, original, measurementUnitId, CreatedAt);
        blueprint.ClearDomainEvents();

        blueprint.UpdateOption(101, updated, measurementUnitId, ChangedAt);

        blueprint.Options.Single().Value.Should().Be(original);
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Text_Casing_Can_Be_Corrected_On_The_Same_Option()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "cotton", null, CreatedAt);
        blueprint.ClearDomainEvents();

        blueprint.UpdateOption(101, "Cotton", null, ChangedAt);

        blueprint.Options.Single().Value.Should().Be("Cotton");
        blueprint.UpdatedAt.Should().Be(ChangedAt);
        blueprint.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<AttributeBlueprintOptionUpdatedDomainEvent>();
    }

    [Fact]
    public void Unit_Only_Update_Is_A_Change()
    {
        var blueprint = Create(AttributeBlueprintDataType.Number);
        blueprint.AddOption(101, "5", 1, CreatedAt);
        blueprint.ClearDomainEvents();

        blueprint.UpdateOption(101, "5", 2, ChangedAt);

        blueprint.Options.Single().MeasurementUnitId.Should().Be(2);
        blueprint.UpdatedAt.Should().Be(ChangedAt);
        blueprint.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<AttributeBlueprintOptionUpdatedDomainEvent>();
    }

    [Fact]
    public void Remove_Only_Removes_The_Target_And_Raises_Event()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Cotton", null, CreatedAt);
        blueprint.AddOption(102, "Wool", null, CreatedAt);
        blueprint.ClearDomainEvents();

        blueprint.RemoveOption(101, ChangedAt);

        blueprint.Options.Should().ContainSingle().Subject.Id.Should().Be(102);
        blueprint.UpdatedAt.Should().Be(ChangedAt);
        var removed = blueprint.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<AttributeBlueprintOptionRemovedDomainEvent>().Subject;
        removed.AttributeBlueprintId.Should().Be(42);
        removed.OptionId.Should().Be(101);
    }

    [Fact]
    public void Unknown_Or_Other_Blueprints_Option_Cannot_Be_Updated_Or_Removed()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        var other = AttributeBlueprint.Create(43, "Other", AttributeBlueprintDataType.Text, CreatedAt);
        other.AddOption(101, "Cotton", null, CreatedAt);
        Action update = () => blueprint.UpdateOption(101, "Wool", null, ChangedAt);
        Action remove = () => blueprint.RemoveOption(101, ChangedAt);

        foreach (var act in new[] { update, remove })
            act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_NOT_FOUND");

        blueprint.Options.Should().BeEmpty();
        blueprint.UpdatedAt.Should().BeNull();
        blueprint.DomainEvents.Should().BeEmpty();
        other.Options.Single().Value.Should().Be("Cotton");
    }

    [Fact]
    public void Deleted_Blueprint_Rejects_All_Option_Mutations()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Cotton", null, CreatedAt);
        blueprint.Delete(CreatedAt);
        blueprint.ClearDomainEvents();
        Action add = () => blueprint.AddOption(102, "Wool", null, ChangedAt);
        Action update = () => blueprint.UpdateOption(101, "Wool", null, ChangedAt);
        Action remove = () => blueprint.RemoveOption(101, ChangedAt);

        foreach (var act in new[] { add, update, remove })
            act.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DELETED");

        blueprint.Options.Should().ContainSingle().Subject.Value.Should().Be("Cotton");
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inactive_And_Archived_Blueprints_Keep_Existing_Metadata_Editing_Behavior(bool archived)
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.Deactivate(CreatedAt);
        if (archived) blueprint.Archive(CreatedAt);

        blueprint.AddOption(101, "Cotton", null, ChangedAt);
        blueprint.UpdateOption(101, "Wool", null, ChangedAt);
        blueprint.RemoveOption(101, ChangedAt);

        blueprint.Options.Should().BeEmpty();
        blueprint.Status.Should().Be(archived ? AttributeBlueprintStatus.Archived : AttributeBlueprintStatus.Inactive);
    }

    [Fact]
    public void Type_Change_Is_Blocked_Until_Options_Are_Removed_But_Same_Type_Is_NoOp()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Cotton", null, CreatedAt);
        blueprint.ClearDomainEvents();

        blueprint.ChangeDataType(AttributeBlueprintDataType.Text, ChangedAt);
        Action change = () => blueprint.ChangeDataType(AttributeBlueprintDataType.Boolean, ChangedAt);
        change.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DATA_TYPE_HAS_OPTIONS");
        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Text);
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();

        blueprint.RemoveOption(101, ChangedAt);
        blueprint.ChangeDataType(AttributeBlueprintDataType.Boolean, ChangedAt);
        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Boolean);
    }

    [Fact]
    public void Full_Update_Allows_Value_Swap_And_Keeps_Existing_Options()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Red", null, CreatedAt);
        blueprint.AddOption(102, "Blue", null, CreatedAt);
        var red = blueprint.Options.Single(option => option.Id == 101);
        var blue = blueprint.Options.Single(option => option.Id == 102);
        blueprint.ClearDomainEvents();

        blueprint.UpdateDetails("Attribute", AttributeBlueprintDataType.Text, true,
            [(101, "Blue", null), (102, "Red", null)], ChangedAt);

        blueprint.Options.Single(option => option.Id == 101).Should().BeSameAs(red);
        blueprint.Options.Single(option => option.Id == 102).Should().BeSameAs(blue);
        red.Value.Should().Be("Blue");
        blue.Value.Should().Be("Red");
        blueprint.UpdatedAt.Should().Be(ChangedAt);
        blueprint.DomainEvents.Should().HaveCount(2).And.OnlyContain(
            domainEvent => domainEvent is AttributeBlueprintOptionUpdatedDomainEvent);
    }

    [Fact]
    public void Invalid_Full_Update_Leaves_All_State_And_Events_Unchanged()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Red", null, CreatedAt);
        blueprint.AddOption(102, "Blue", null, CreatedAt);
        blueprint.AddOption(103, "Green", null, CreatedAt);
        blueprint.ClearDomainEvents();

        Action update = () => blueprint.UpdateDetails("Changed", AttributeBlueprintDataType.Text, false,
            [(101, "Blue", null), (102, " blue ", null)], ChangedAt);

        update.Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TAKEN");
        blueprint.Name.Should().Be("Attribute");
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Active);
        blueprint.Options.Select(option => (option.Id, option.Value)).Should().Equal(
            (101L, "Red"), (102L, "Blue"), (103L, "Green"));
        blueprint.UpdatedAt.Should().Be(CreatedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Full_Update_Changes_Type_Without_Replacing_Existing_Option()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Five", null, CreatedAt);
        var option = blueprint.Options.Single();
        blueprint.ClearDomainEvents();

        blueprint.UpdateDetails("Attribute", AttributeBlueprintDataType.Number, true,
            [(101, "5.00", 7)], ChangedAt);

        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Number);
        blueprint.Options.Should().ContainSingle().Subject.Should().BeSameAs(option);
        option.Value.Should().Be("5");
        option.MeasurementUnitId.Should().Be(7);
        blueprint.DomainEvents.Should().ContainSingle(domainEvent => domainEvent is AttributeBlueprintDataTypeChangedDomainEvent);
        blueprint.DomainEvents.Should().ContainSingle(domainEvent => domainEvent is AttributeBlueprintOptionUpdatedDomainEvent);
    }

    [Fact]
    public void Options_Cannot_Be_Changed_Through_The_Public_Collection()
    {
        var blueprint = Create(AttributeBlueprintDataType.Text);
        blueprint.AddOption(101, "Cotton", null, CreatedAt);

        Action clear = () => ((ICollection<AttributeBlueprintOption>)blueprint.Options).Clear();

        clear.Should().Throw<NotSupportedException>();
        blueprint.Options.Should().ContainSingle();
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("fa-IR")]
    public void Numeric_And_Date_Formats_Do_Not_Depend_On_Current_Culture(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var number = Create(AttributeBlueprintDataType.Number);
            var date = Create(AttributeBlueprintDataType.Date);

            number.AddOption(101, "+005.50", 1, ChangedAt);
            date.AddOption(102, "2026-09-16", null, ChangedAt);

            number.Options.Single().Value.Should().Be("5.5");
            date.Options.Single().Value.Should().Be("2026-09-16");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static AttributeBlueprint Create(AttributeBlueprintDataType type)
    {
        var blueprint = AttributeBlueprint.Create(42, "Attribute", type, CreatedAt);
        blueprint.ClearDomainEvents();
        return blueprint;
    }
}
