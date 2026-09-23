namespace AskLucy.Web.Contracts;

/// <summary>specs/072 contracts/admin-custom-models.md <c>POST api/v1/admin/custom-models</c>. <c>Name</c> is needed only when the source's derived name is missing or taken.</summary>
public sealed record SubmitCustomModelRequest(string Source, string Destination, string? Name);

/// <summary>specs/072 <c>POST source-preview</c>. Parsed only; nothing is fetched.</summary>
public sealed record PreviewCustomModelSourceRequest(string? Source);

/// <summary>specs/072 <c>PUT {id}/availability</c>.</summary>
public sealed record SetCustomModelAvailabilityRequest(AskLucy.Domain.CustomModels.CustomModelAvailability Availability);
