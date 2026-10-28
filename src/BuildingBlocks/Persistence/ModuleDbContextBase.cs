using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Persistence;

public abstract class ModuleDbContextBase(DbContextOptions options, string schema) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                foreach (var property in entityType.GetProperties().Where(p => p.Name == "RowVersion"))
                {
                    property.IsConcurrencyToken = false;
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                }
        }

        base.OnModelCreating(modelBuilder);
    }
}
