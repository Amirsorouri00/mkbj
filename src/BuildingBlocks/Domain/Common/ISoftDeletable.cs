namespace EwaysVNext.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
