using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class ResumeTemplateChoiceTests
{
    private static ResumeVersion Version() =>
        ResumeVersion.Propose(Guid.NewGuid(), "master.docx", [new ResumeChange("SUMMARY", "old", "new", [])], "DE");

    [Fact]
    public void DefaultTemplate_KeepsLegacyPayload() =>
        Assert.Equal("SUMMARY|old|new", Version().PayloadForApproval());

    [Fact]
    public void ChangingTemplate_ChangesPayloadAndResetsApproval()
    {
        var approved = Version().WithApproval(Guid.NewGuid()).MarkApproved();
        var switched = approved.WithTemplate(ResumeTemplateChoice.CountryTemplate, "master-DE.docx");
        Assert.NotEqual(approved.PayloadForApproval(), switched.PayloadForApproval());
        Assert.Null(switched.ApprovalId);
        Assert.Equal(ResumeVersionStatus.PROPOSED, switched.Status);
        Assert.Equal("master-DE.docx", switched.MasterPath);
    }
}
