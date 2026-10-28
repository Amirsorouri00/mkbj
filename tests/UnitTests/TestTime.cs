namespace EwaysVNext.UnitTests;

internal static class TestTime
{
    public static readonly DateTimeOffset Now =
        new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Plus(TimeSpan elapsed) => Now + elapsed;
    public static DateTimeOffset PlusMinutes(int minutes) => Now.AddMinutes(minutes);
    public static DateTimeOffset PlusDays(int days) => Now.AddDays(days);
}
