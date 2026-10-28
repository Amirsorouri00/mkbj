using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using Xunit;

namespace EwaysVNext.UnitTests;

public class SnowflakeIdGeneratorTests
{
    [Fact]
    public void Ids_From_One_Generator_Are_Positive_Unique_And_Strictly_Increasing_Within_A_Millisecond()
    {
        var generator = new SnowflakeIdGenerator(new FixedClock());

        var ids = Enumerable.Range(0, 10_000).Select(_ => generator.Generate()).ToList();

        ids.Should().OnlyContain(id => id > 0);
        ids.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void An_Id_Carries_The_Millisecond_It_Was_Generated_In()
    {
        var generator = new SnowflakeIdGenerator(new FixedClock(TestTime.Now.AddTicks(1234)));

        var id = generator.Generate();

        SnowflakeIdGenerator.GetCreatedAt(id).Should().Be(TestTime.Now);
    }

    [Fact]
    public void Separate_Generators_Do_Not_Share_A_Fixed_Node_Or_Sequence_Start()
    {
        var firstIds = Enumerable.Range(0, 32)
            .Select(_ => new SnowflakeIdGenerator(new FixedClock()).Generate())
            .ToList();

        firstIds.Distinct().Should().HaveCountGreaterThan(1,
            "with no worker id, the per-millisecond random sequence start is the only thing " +
            "keeping two instances that generate in the same millisecond apart");
    }

    [Fact]
    public void A_Clock_That_Moves_Backwards_Neither_Throws_Nor_Breaks_Ordering()
    {
        var clock = new FixedClock();
        var generator = new SnowflakeIdGenerator(clock);
        var before = generator.Generate();

        clock.Advance(TimeSpan.FromSeconds(-5));
        var after = generator.Generate();

        after.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Exhausting_A_Millisecond_Borrows_The_Next_One_Instead_Of_Blocking()
    {
        var generator = new SnowflakeIdGenerator(new FixedClock());
        var previous = generator.Generate();
        var last = previous;

        for (var i = 0; i <= SnowflakeIdGenerator.MaxSequence; i++)
        {
            last = generator.Generate();
            if (last <= previous) Assert.Fail($"id {last} did not increase past {previous}");
            previous = last;
        }

        SnowflakeIdGenerator.GetCreatedAt(last).Should().BeAfter(TestTime.Now);
    }

    [Fact]
    public void A_Clock_Earlier_Than_The_Epoch_Is_Refused()
    {
        var generator = new SnowflakeIdGenerator(new FixedClock(SnowflakeIdGenerator.Epoch.AddMilliseconds(-1)));

        var act = () => generator.Generate();

        act.Should().Throw<InvalidOperationException>();
    }
}
