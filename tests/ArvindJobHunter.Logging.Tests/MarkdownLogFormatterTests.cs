using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging.Tests;

public sealed class MarkdownLogFormatterTests
{
    [Theory]
    [InlineData("ArvindJobHunter.Application.Features.ExecuteApprovedActionCommand", "Features.ExecuteApprovedActionCommand")]
    [InlineData("Microsoft.Hosting.Lifetime", "Hosting.Lifetime")]
    [InlineData("Program", "Program")]
    [InlineData("Web.Client", "Web.Client")]
    public void ShortCategory_KeepsTheLastTwoSegments(string category, string expected) =>
        Assert.Equal(expected, MarkdownLogFormatter.ShortCategory(category));

    [Theory]
    [InlineData(LogLevel.Information, "ℹ️ INFO")]
    [InlineData(LogLevel.Warning, "⚠️ WARN")]
    [InlineData(LogLevel.Error, "❌ **ERROR**")]
    [InlineData(LogLevel.Critical, "🔥 **CRITICAL**")]
    public void LevelLabel_IsReadable(LogLevel level, string expected) => Assert.Equal(expected, MarkdownLogFormatter.LevelLabel(level));

    [Fact]
    public void EscapeCell_KeepsOnePhysicalLine_AndNeutralisesHtml()
    {
        Assert.Equal("x \\| y<br>z &lt;i&gt;", MarkdownLogFormatter.EscapeCell("  x | y\r\nz <i>\u0007 "));
    }

    [Fact]
    public void ExceptionDetails_UsesALongerFence_WhenTheTextContainsBackticks()
    {
        var entry = new MarkdownLogEntry(DateTimeOffset.UnixEpoch, LogLevel.Error, "c", "m", null, null);
        var details = MarkdownLogFormatter.ExceptionDetails(entry, "Error with ``` inside");
        Assert.Contains("````text\nError with ``` inside\n````", details);
    }

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOi.payload.sig", "Authorization: Bearer ***")]
    [InlineData("key=sk-abcdefghijklmnop1234", "key=sk-***")]
    [InlineData("{\"clientSecret\": \"not-a-real-secret\", \"clientId\": \"abc\"}", "{\"clientSecret\": \"***\", \"clientId\": \"abc\"}")]
    [InlineData("/callback?state=abc&code=xyz#frag", "/callback?state=***&code=***#frag")]
    [InlineData("LlmApiKey=abc123; Model=gpt", "LlmApiKey=***; Model=gpt")]
    [InlineData("X-Worker-Secret: 0123456789ABCDEF", "X-Worker-Secret: ***")]
    [InlineData("Password reset performed for 'arvind'", "Password reset performed for 'arvind'")]
    [InlineData("Mode=LIVE; Llm=OpenAI; ClientIdLength=72", "Mode=LIVE; Llm=OpenAI; ClientIdLength=72")]
    public void Redactor_MasksSecrets_AndLeavesOrdinaryTextAlone(string input, string expected) =>
        Assert.Equal(expected, LogRedactor.Redact(input));
}
