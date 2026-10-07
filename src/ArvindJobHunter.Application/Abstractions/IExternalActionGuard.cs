using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Abstractions;

/// <summary>Final gate before any side effect. Verifies approval identity, hash, expiry, action type, and execution mode.</summary>
public interface IExternalActionGuard
{
    void EnsureAllowed(ApprovalRequest approval, ApprovalActionType action, string payloadHash, ExecutionMode currentMode);
}
