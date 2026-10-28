namespace EwaysVNext.Domain.Common;

public static class Guard
{
    public static class Against
    {
        public static T Null<T>(T? value, Func<Exception> exceptionFactory) where T : class
            => value ?? throw exceptionFactory();

        public static long Zero(long value, Func<Exception> exceptionFactory)
            => value == 0 ? throw exceptionFactory() : value;

        public static decimal Zero(decimal value, Func<Exception> exceptionFactory)
            => value == 0 ? throw exceptionFactory() : value;

        public static bool False(bool value, Func<Exception> exceptionFactory)
            => !value ? throw exceptionFactory() : value;

        public static bool True(bool value, Func<Exception> exceptionFactory)
            => value ? throw exceptionFactory() : value;

        public static string NullOrWhiteSpace(string? value, Func<Exception> exceptionFactory)
            => string.IsNullOrWhiteSpace(value) ? throw exceptionFactory() : value;

        public static string NullOrEmpty(string? value, Func<Exception> exceptionFactory)
            => string.IsNullOrEmpty(value) ? throw exceptionFactory() : value;

        public static long GreaterThan(long value, long comparand, Func<Exception> exceptionFactory)
            => value > comparand ? throw exceptionFactory() : value;

        public static long LessThan(long value, long comparand, Func<Exception> exceptionFactory)
            => value < comparand ? throw exceptionFactory() : value;

        public static long OutOfRange(long value, long start, long end, Func<Exception> exceptionFactory)
            => value < start || value > end ? throw exceptionFactory() : value;
    }
}
