namespace ArvindJobHunter.Domain.Entities;

public sealed record ExecutionReceipt(
    Guid Id,
    Guid ApprovalId,
    string IdempotencyKey,
    ApprovalActionType ActionType,
    ExecutionMode Mode,
    ExecutionResult Result,
    string? ExternalReference,
    string? Message,
    DateTimeOffset ExecutedAt);
