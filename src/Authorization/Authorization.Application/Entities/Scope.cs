namespace AuthorizationService.Application.Entities;

public class Scope
{
    private Scope()
    {
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Scope Create(string code, string name)
    {
        var now = DateTime.UtcNow;
        return new Scope
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void Update(string name)
    {
        Name = name;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
