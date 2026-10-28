using System.Text;

namespace EwaysVNext.Domain.ValueObjects;

internal static class EmailMessages
{
    public const string Empty = "Email cannot be empty.";
    public const string TooLong = "Email cannot exceed 256 characters.";
    public const string InvalidFormat = "'{0}' is not a valid email.";
    public static readonly CompositeFormat InvalidComposite = CompositeFormat.Parse(InvalidFormat);
}
