namespace ArvindJobHunter.Domain.Entities;

public sealed class CandidateSkill
{
    private CandidateSkill()
    {
    }

    public CandidateSkill(Guid id, Guid candidateProfileId, string name, string category)
    {
        Id = id;
        CandidateProfileId = candidateProfileId;
        Name = name.Trim();
        Category = category.Trim();
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public CandidateProfile? CandidateProfile { get; private set; }
}
