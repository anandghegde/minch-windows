namespace Minch.Core.Api;

public static class TorBoxIds
{
    public const string WebDlPrefix = "webdl:";

    /// <summary>"webdl:42" → "42"; torrent ids → null.</summary>
    public static string? SplitWebDl(string transferId) =>
        transferId.StartsWith(WebDlPrefix, StringComparison.Ordinal) ? transferId[WebDlPrefix.Length..] : null;

    /// <summary>Composite file id "&lt;transfer&gt;:&lt;file&gt;" → trailing raw TorBox id.</summary>
    public static string RawFileId(string fileId)
    {
        var idx = fileId.LastIndexOf(':');
        return idx >= 0 ? fileId[(idx + 1)..] : fileId;
    }

    /// <summary>Maps a (possibly webdl-namespaced) transfer id to the (id, type) stream endpoints expect.</summary>
    public static (string Id, string Type) StreamRoute(string transferId) =>
        SplitWebDl(transferId) is { } web ? (web, "webdownload") : (transferId, "torrent");
}
