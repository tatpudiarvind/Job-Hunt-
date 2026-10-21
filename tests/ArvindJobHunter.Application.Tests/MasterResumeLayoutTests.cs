using ArvindJobHunter.Application.Features;

namespace ArvindJobHunter.Application.Tests;

public sealed class MasterResumeLayoutTests
{
    [Theory]
    [InlineData(@"C:\resumes\master.pdf")]
    [InlineData(@"C:\resumes\MASTER.PDF")]
    public void PdfMaster_ReturnsLayoutWarning(string path) =>
        Assert.Equal(MasterResumeLayout.PdfWarning, MasterResumeLayout.WarningFor(path));

    [Theory]
    [InlineData(@"C:\resumes\master.docx")]
    [InlineData("")]
    [InlineData(null)]
    public void WordOrMissingMaster_ReturnsNoWarning(string? path) =>
        Assert.Null(MasterResumeLayout.WarningFor(path));
}
