namespace EwaysVNext.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    int SaveChanges();
}
