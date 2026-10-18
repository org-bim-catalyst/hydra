namespace AskLucy.Domain.Ai;

public enum ModelDeprecationKind
{
    Deprecation,
    ReplacementChange,
}

public enum UserImpactStatus
{
    Pending,
    Completed,
}

public enum DeprecationReason
{
    RemovedFromVendor,
}

public enum ReplacementSource
{
    Proposed,
    AdminChosen,
    None,
}

public enum ReplacementValidationStatus
{
    AwaitingValidation,
    Accepted,
    Changed,
    NotApplicable,
}

public enum DefaultTargetKind
{
    ProviderDefault,
    CapabilityAssignment,
}

public enum DefaultChangeOutcome
{
    Reassigned,
    Flagged,
}

public enum DefaultFlagReason
{
    NoEligibleReplacement,
    MissingRequiredCapability,
    AdminChoseNone,
}

public enum SwitchedItemKind
{
    Conversation,
    Agent,
    Prompt,
    WorkflowStep,
    UserDefault,
}
