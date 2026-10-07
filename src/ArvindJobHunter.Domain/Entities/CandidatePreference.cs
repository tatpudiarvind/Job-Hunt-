namespace ArvindJobHunter.Domain.Entities;

public sealed class CandidatePreference
{
    private CandidatePreference()
    {
    }

    public CandidatePreference(Guid id, Guid candidateProfileId, string key, string value)
    {
        Id = id;
        CandidateProfileId = candidateProfileId;
        Key = key.Trim();
        Value = value.Trim();
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public CandidateProfile? CandidateProfile { get; private set; }
}
