using ArvindJobHunter.Worker.Browser;

namespace ArvindJobHunter.Worker.Tests;

public sealed class FieldMatcherTests
{
    private static PageField Field(string label, string kind = "text", params string[] options) => new(0, label, kind, true, options);

    [Theory]
    [InlineData("Given name *", "First name")]
    [InlineData("Surname", "Last name")]
    [InlineData("E-mail address", "Email")]
    [InlineData("Mobile number", "Phone")]
    [InlineData("LinkedIn profile URL", "LinkedIn")]
    [InlineData("Will you now or in the future require sponsorship?", "Sponsorship")]
    [InlineData("Are you legally authorized to work in the UK?", "Work authorization")]
    [InlineData("Favourite colour", "Favourite colour")]
    public void Canonical_MapsCommonLabels(string label, string expected) => Assert.Equal(expected, FieldMatcher.Canonical(label));

    [Fact]
    public void Answer_PrefersExactLabelThenCanonical()
    {
        var answers = new Dictionary<string, string> { ["Email"] = "a@b.com", ["Why us?"] = "Mission" };
        Assert.Equal("a@b.com", FieldMatcher.Answer(Field("Email address *"), answers));
        Assert.Equal("Mission", FieldMatcher.Answer(Field("Why us? *"), answers));
        Assert.Null(FieldMatcher.Answer(Field("Shoe size"), answers));
    }

    [Fact]
    public void Answer_BuildsFullNameFromParts()
    {
        var answers = new Dictionary<string, string> { ["First name"] = "Arvind", ["Last name"] = "T" };
        Assert.Equal("Arvind T", FieldMatcher.Answer(Field("Full name"), answers));
    }

    [Fact]
    public void PickOption_NeverInventsFreeText()
    {
        string[] options = ["Yes", "No"];
        Assert.Equal("No", FieldMatcher.PickOption(options, "no"));
        Assert.Null(FieldMatcher.PickOption(options, "Maybe"));
    }

    [Fact]
    public void Uploads_AreClassifiedByLabel()
    {
        Assert.True(FieldMatcher.IsResumeUpload(Field("Resume/CV", "file")));
        Assert.True(FieldMatcher.IsCoverLetterUpload(Field("Cover letter", "file")));
        Assert.False(FieldMatcher.IsResumeUpload(Field("Cover letter", "file")));
    }
}
