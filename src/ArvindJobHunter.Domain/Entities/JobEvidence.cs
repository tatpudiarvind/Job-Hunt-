namespace ArvindJobHunter.Domain.Entities;

public enum SponsorshipStatus
{
    UNKNOWN,
    MENTIONED,
    EXCLUDED
}

/// <summary>Where a sponsorship signal came from. Keyword evidence is a hint, never a guarantee.</summary>
public sealed record SponsorshipEvidence(
    SponsorshipStatus Status,
    string? MatchedPhrase,
    string Source,
    DateTimeOffset CheckedAt)
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(30);

    public bool IsStale(DateTimeOffset now) => now - CheckedAt > FreshFor;
}

public enum RecruiterContactStatus
{
    NOT_FOUND,
    USER_PROVIDED,
    VERIFIED
}

/// <summary>A recruiter contact is only ever recorded from a user-supplied source; it is never guessed.</summary>
public sealed record RecruiterContact(
    RecruiterContactStatus Status,
    string? Name,
    string? Email,
    string? Source,
    DateTimeOffset RecordedAt)
{
    public static RecruiterContact Create(RecruiterContactStatus status, string? name, string? email, string? source)
    {
        var trimmedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (status != RecruiterContactStatus.NOT_FOUND)
        {
            if (trimmedEmail is null || !trimmedEmail.Contains('@') || trimmedEmail.StartsWith('@') || trimmedEmail.EndsWith('@'))
                throw new ArgumentException("A valid recruiter email is required.", nameof(email));
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Record where the contact came from.", nameof(source));
        }
        else
        {
            trimmedEmail = null;
        }

        return new RecruiterContact(status, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), trimmedEmail,
            string.IsNullOrWhiteSpace(source) ? null : source.Trim(), DateTimeOffset.UtcNow);
    }

    public bool IsUsable => Status != RecruiterContactStatus.NOT_FOUND && Email is not null;
}

/// <summary>An immutable snapshot of what is about to be submitted, bound by content hashes.</summary>
public sealed record ApplicationPackage(
    Guid Id,
    Guid ApplicationId,
    Guid JobId,
    int Version,
    string JobSnapshotHash,
    Guid? ResumeVersionId,
    string? ResumeHash,
    Guid? CoverLetterDraftId,
    string? CoverLetterHash,
    string PackageHash,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    string? ResumeFilePath = null,
    string? ResumeFileSha256 = null,
    string? CoverLetterFilePath = null,
    string? CoverLetterFileSha256 = null);
