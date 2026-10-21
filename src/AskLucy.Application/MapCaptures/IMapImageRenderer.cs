namespace AskLucy.Application.MapCaptures;

/// <summary>
/// Renders one map capture. Implementations never throw for provider failures — they return a
/// typed <see cref="MapImageResult.Failure"/> so the controller can map it to ProblemDetails.
/// </summary>
public interface IMapImageRenderer
{
    Task<MapImageResult> RenderAsync(MapImageRequest request, CancellationToken cancellationToken = default);
}
