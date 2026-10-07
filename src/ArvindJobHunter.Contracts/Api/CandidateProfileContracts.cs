using System.ComponentModel.DataAnnotations;

namespace ArvindJobHunter.Contracts.Api;

/// <summary>Payload for creating or updating the verified candidate profile.</summary>
public sealed record UpdateCandidateProfileRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, MaxLength(200)]
    public required string CurrentRole { get; init; }

    [MaxLength(200)]
    public string? CurrentCompany { get; init; }
}

/// <summary>Verified candidate profile returned by the API.</summary>
public sealed record CandidateProfileResponse(
    Guid Id,
    string Name,
    string CurrentRole,
    string? CurrentCompany,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version);
