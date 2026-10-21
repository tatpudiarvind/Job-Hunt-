using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Tests;

public sealed class PackageFileTests
{
    private static ApplicationPackage Package(string? path, string? sha) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "j", Guid.NewGuid(), "r", null, null, "p", Guid.NewGuid(), DateTimeOffset.UtcNow, path, sha);

    [Fact]
    public void VerifyFiles_DetectsChangedAndMissingFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "resume");
            var package = Package(path, ApplicationPackageService.FileHash(path));
            Assert.Empty(ApplicationPackageService.VerifyFiles(package));

            File.WriteAllText(path, "tampered");
            Assert.Contains(ApplicationPackageService.VerifyFiles(package), i => i.Contains("changed"));

            File.Delete(path);
            Assert.Contains(ApplicationPackageService.VerifyFiles(package), i => i.Contains("no longer exists"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void VerifyFiles_FlagsMissingResumeFile() =>
        Assert.Single(ApplicationPackageService.VerifyFiles(Package(null, null)));

    [Fact]
    public void UploadFile_PrefersExistingPdf()
    {
        var docx = Path.GetTempFileName();
        try
        {
            Assert.Equal(docx, ApplicationPackageService.UploadFile("missing.pdf", docx));
            Assert.Null(ApplicationPackageService.UploadFile(null, "missing.docx"));
        }
        finally { File.Delete(docx); }
    }
}
