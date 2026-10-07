using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Infrastructure.Persistence;

public sealed class CandidateProfileDocument
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CurrentRole { get; init; } = string.Empty;
    public string? CurrentCompany { get; init; }

    public static CandidateProfileDocument From(CandidateProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        CurrentRole = profile.CurrentRole,
        CurrentCompany = profile.CurrentCompany
    };

    public CandidateProfile ToDomain() => new(Id!.Value, Name, CurrentRole, CurrentCompany);
}