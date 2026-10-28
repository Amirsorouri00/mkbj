namespace EwaysVNext.Domain.Common;

public interface IAuditable
{
    string OccurredBy { get; set; }
    DateTimeOffset OccurredOn { get; set; }
    string? ModifiedBy { get; set; }
    DateTimeOffset? ModifiedAt { get; set; }
}
