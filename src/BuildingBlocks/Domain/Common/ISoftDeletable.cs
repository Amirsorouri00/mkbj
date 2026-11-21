namespace Mkbj.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
