namespace ArvindJobHunter.Domain.Entities;

public sealed record CandidateFact(
    Guid Id,
    string FactType,
    string Name,
    string Value,
    string SourceType,
    string SourceReference,
    bool IsVerified,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CandidateFact Create(string factType, string name, string value, string sourceReference, string sourceType = "MANUAL")
    {
        if (string.IsNullOrWhiteSpace(factType)) throw new ArgumentException("Fact type is required.", nameof(factType));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Fact name is required.", nameof(name));
        var now = DateTimeOffset.UtcNow;
        return new CandidateFact(Guid.NewGuid(), factType.Trim().ToUpperInvariant(), name.Trim(), value.Trim(), sourceType, sourceReference.Trim(), false, null, now, now);
    }

    public CandidateFact SetVerification(bool verified)
    {
        var now = DateTimeOffset.UtcNow;
        return this with { IsVerified = verified, VerifiedAt = verified ? now : null, UpdatedAt = now };
    }
}
