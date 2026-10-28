namespace EwaysVNext.Jobs.Implementation;

/// <summary>
/// Stored as its name, with a check constraint in the database so an unknown value cannot be
/// written by anything — including hand-written SQL.
/// </summary>
public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}
