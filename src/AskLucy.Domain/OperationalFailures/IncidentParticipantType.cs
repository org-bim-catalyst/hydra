namespace AskLucy.Domain.OperationalFailures;

/// <summary>What a participant key identifies: an affected account, or an Access-engine source address (research D10).</summary>
public enum IncidentParticipantType
{
    User,
    Source,
}
