namespace AskLucy.Web.Contracts;

/// <summary>specs/082 contracts "PUT": the new retention window in whole days (1–365).</summary>
public sealed record UpdateProviderHealthRetentionRequest(int RetentionDays);
