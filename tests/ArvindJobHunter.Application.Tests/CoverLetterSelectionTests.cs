using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Tests;

public sealed class CoverLetterSelectionTests
{
    private static readonly Job Posting = Job.Create("Senior .NET Engineer", "Contoso", "Berlin, Germany", "MANUAL", null, "C#, Azure and Kubernetes.");

    [Fact]
    public void SelectCoverLetter_PicksBestKeywordMatch()
    {
        var choice = ResumeSelector.SelectCoverLetter(Posting,
        [
            new ResumeCandidate("generic.pdf", "Generic", ["python"]),
            new ResumeCandidate("dotnet.pdf", ".NET", ["C#", "Azure"]),
            new ResumeCandidate("contoso.pdf", "Contoso", ["Contoso"])
        ], _ => true);

        Assert.Equal("dotnet.pdf", choice!.Path);
    }

    [Fact]
    public void SelectCoverLetter_NoMatchOrMissingFile_ReturnsNull()
    {
        Assert.Null(ResumeSelector.SelectCoverLetter(Posting, [new ResumeCandidate("x.pdf", "Data", ["python"])], _ => true));
        Assert.Null(ResumeSelector.SelectCoverLetter(Posting, [new ResumeCandidate("gone.pdf", ".NET", ["C#"])], _ => false));
    }
}
