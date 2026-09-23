namespace AskLucy.Domain.CustomModels;

/// <summary>
/// An action that needs a deployment in progress (Cancel) was asked of one that already finished.
/// Mapped to 409, since it's the record's current state that refuses, not the request's shape.
/// </summary>
public sealed class CustomModelNotInProgressException(string message) : Exception(message);
