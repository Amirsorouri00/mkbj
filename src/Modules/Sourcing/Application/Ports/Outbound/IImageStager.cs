namespace Mkbj.Modules.Sourcing.Application.Ports.Outbound;

/// <summary>Downloads externally referenced product images into private import staging.</summary>
public interface IImageStager
{
    Task<IReadOnlyList<string>> StageAsync(
        IReadOnlyList<string> imageUrls, int max, CancellationToken ct = default);
}
