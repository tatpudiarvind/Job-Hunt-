namespace ArvindJobHunter.Domain.Entities;

public sealed class CandidateProfile
{
    private CandidateProfile()
    {
    }

    public CandidateProfile(Guid id, string name, string currentRole, string? currentCompany)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Candidate profile id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Candidate name is required.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
        CurrentRole = currentRole.Trim();
        CurrentCompany = string.IsNullOrWhiteSpace(currentCompany) ? null : currentCompany.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string CurrentRole { get; private set; } = string.Empty;
    public string? CurrentCompany { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();
    public List<CandidateSkill> Skills { get; private set; } = [];
    public List<CandidatePreference> Preferences { get; private set; } = [];

    public void Update(string name, string currentRole, string? currentCompany)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currentRole))
        {
            throw new ArgumentException("Name and current role are required.");
        }

        Name = name.Trim();
        CurrentRole = currentRole.Trim();
        CurrentCompany = string.IsNullOrWhiteSpace(currentCompany) ? null : currentCompany.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
