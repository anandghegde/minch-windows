namespace Minch.Core.Api;

public enum ApiErrorKind
{
    Auth,
    Transient,
    Validation,
    Quota,
    Decoding,
    Network,
    Unknown,
}

public sealed class ApiException : Exception
{
    public ApiErrorKind Kind { get; }
    public int? StatusCode { get; }
    public string Detail { get; }

    public ApiException(ApiErrorKind kind, string detail, int? statusCode = null, Exception? inner = null)
        : base($"{kind}: {detail}", inner)
    {
        Kind = kind;
        Detail = detail;
        StatusCode = statusCode;
    }

    /// <summary>Short, user-facing text. Port of AppModel.friendlyMessage on macOS.</summary>
    public string FriendlyMessage
    {
        get
        {
            var trimmed = Detail.Trim();
            return Kind switch
            {
                ApiErrorKind.Auth => trimmed.Length == 0 || trimmed.StartsWith("HTTP ", StringComparison.Ordinal)
                    ? "That key was rejected by TorBox. Double-check it and try again."
                    : $"TorBox rejected this request: {trimmed}",
                ApiErrorKind.Quota => $"TorBox is rate-limiting requests ({trimmed}). Wait a moment and retry.",
                ApiErrorKind.Transient => trimmed.Length == 0 || trimmed.StartsWith("HTTP ", StringComparison.Ordinal)
                    ? "TorBox is temporarily unavailable. Try again shortly."
                    : $"TorBox is temporarily unavailable: {trimmed}",
                ApiErrorKind.Validation => trimmed.Length == 0 ? "TorBox didn't accept that request." : trimmed,
                ApiErrorKind.Decoding => "Unexpected response from TorBox.",
                ApiErrorKind.Network => "Couldn't reach TorBox. Check your connection and try again.",
                _ => trimmed.Length == 0 ? $"Unexpected response (HTTP {StatusCode})." : trimmed,
            };
        }
    }

    public static string Friendly(Exception ex, string fallback) =>
        ex is ApiException api ? api.FriendlyMessage : fallback;
}
