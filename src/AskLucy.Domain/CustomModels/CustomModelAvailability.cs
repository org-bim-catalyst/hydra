namespace AskLucy.Domain.CustomModels;

/// <summary>specs/072 FR-030. Whether the engine bound to the model's repository may use it. Every model starts <see cref="Unavailable"/>.</summary>
public enum CustomModelAvailability
{
    Unavailable,
    Available,
}
