using System.Text.RegularExpressions;

namespace ArvindJobHunter.Logging;

/// <summary>Masks secrets that must never reach a log file, whatever component produced the message.</summary>
internal static partial class LogRedactor
{
    public const string Mask = "***";

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        value = BearerToken().Replace(value, "$1 " + Mask);
        value = OpenAiKey().Replace(value, "sk-" + Mask);
        value = JsonSecret().Replace(value, "$1" + Mask + "$2");
        value = QuerySecret().Replace(value, "$1" + Mask);
        return KeyValueSecret().Replace(value, "$1" + Mask);
    }

    [GeneratedRegex(@"\b(Bearer)\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{8,}")]
    private static partial Regex OpenAiKey();

    [GeneratedRegex("(\"(?:password|newPassword|apiKey|api_key|llmApiKey|clientSecret|client_secret|sharedSecret|secret|token|access_token|refresh_token|id_token|protectedRefreshToken|passwordHash|salt)\"\\s*:\\s*\")[^\"]*(\")", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();

    [GeneratedRegex(@"([?&](?:code|state|token|access_token|refresh_token|id_token|client_secret|api_key|apikey|key|password)=)[^&\s#""']+", RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecret();

    [GeneratedRegex(@"\b((?:(?:llm[_-]?)?api[_-]?key|(?:new[_-]?)?password|client[_-]?secret|shared[_-]?secret|refresh[_-]?token|access[_-]?token|x-worker-secret)\s*[=:]\s*)[^\s;,&""']+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecret();
}
