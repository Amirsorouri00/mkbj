using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

public class CategoryAttributeExclusionTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static long NextId() => IdGenerator.Generate();

    [Fact]
    public void Create_Sets_Category_And_Source_And_Raises_Event()
    {
        var categoryId = NextId();
        var sourceAssignmentId = NextId();

        var exclusion = CategoryAttributeExclusion.Create(NextId(), categoryId, sourceAssignmentId, TestTime.Now);

        exclusion.CategoryId.Should().Be(categoryId);
        exclusion.SourceAssignmentId.Should().Be(sourceAssignmentId);
        exclusion.CreatedAt.Should().Be(TestTime.Now);
        exclusion.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CategoryAttributeExcludedDomainEvent>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Rejects_NonPositive_Id(long id)
    {
        var act = () => CategoryAttributeExclusion.Create(id, NextId(), NextId(), TestTime.Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_ID_INVALID");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Rejects_NonPositive_CategoryId(long categoryId)
    {
        var act = () => CategoryAttributeExclusion.Create(NextId(), categoryId, NextId(), TestTime.Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_CATEGORY_INVALID");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Rejects_NonPositive_SourceAssignmentId(long sourceAssignmentId)
    {
        var act = () => CategoryAttributeExclusion.Create(NextId(), NextId(), sourceAssignmentId, TestTime.Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_SOURCE_INVALID");
    }
}
