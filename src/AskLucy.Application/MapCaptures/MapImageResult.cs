namespace AskLucy.Application.MapCaptures;

/// <summary>PNG bytes or a typed, caller-visible failure — never both, never neither.</summary>
public sealed record MapImageResult
{
    public byte[]? Png { get; private init; }

    public int Width { get; private init; }

    public int Height { get; private init; }

    public MapCaptureFailureReason? FailureReason { get; private init; }

    public string? FailureDetail { get; private init; }

    /// <summary>The place name (or "lat,lng") the capture was actually centred on.</summary>
    public string? ResolvedLocation { get; init; }

    public bool IsSuccess => FailureReason is null;

    public static MapImageResult Success(byte[] png, int width, int height) =>
        new() { Png = png, Width = width, Height = height };

    public static MapImageResult Failure(MapCaptureFailureReason reason, string detail) =>
        new() { FailureReason = reason, FailureDetail = detail };
}
