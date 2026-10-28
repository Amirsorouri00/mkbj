namespace AuthorizationService.Application.Entities;

public class Role
{
    private Role()
    {
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public int Rank { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Role Create(string code, string name, int rank)
    {
        var now = DateTime.UtcNow;
        return new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Rank = rank,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void Update(string name, int rank)
    {
        Name = name;
        Rank = rank;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
