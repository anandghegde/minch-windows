namespace Minch.Core.Media;

public enum MediaKind
{
    Video,
    Audio,
    Other,
}

public static class MediaKinds
{
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { "mp4", "m4v", "mov", "mkv", "webm", "avi", "mpg", "mpeg", "ts", "m2ts", "wmv", "flv", "ogv", "vob" };

    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        { "mp3", "m4a", "aac", "flac", "wav", "aiff", "ogg", "opus", "wma" };

    public static MediaKind Detect(string name, string? mime)
    {
        if (mime is not null)
        {
            if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Video;
            if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Audio;
        }
        var ext = Extension(name);
        if (VideoExts.Contains(ext)) return MediaKind.Video;
        if (AudioExts.Contains(ext)) return MediaKind.Audio;
        return MediaKind.Other;
    }

    public static string Extension(string name) => Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
}

/// <summary>
/// Playback routing — port of MediaCapability.swift, with the "safe" lists
/// retargeted at Windows Media Foundation (MediaPlayerElement), which handles
/// MKV/WebM/AVI/TS natively on Windows 11 unlike AVPlayer.
/// </summary>
public sealed record MediaCapability(MediaKind Kind, bool CanPlayLocally, bool CanStream, bool PrefersExternal)
{
    public static readonly IReadOnlySet<string> WindowsSafeVideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "mp4", "m4v", "mov", "mkv", "webm", "avi", "ts", "m2ts", "wmv" };

    public static readonly IReadOnlySet<string> WindowsSafeAudioExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "mp3", "m4a", "aac", "wav", "flac", "wma" };

    public static MediaCapability Evaluate(string name, string? mime, bool hasLocalFile)
    {
        var kind = MediaKinds.Detect(name, mime);
        if (kind == MediaKind.Other) return new MediaCapability(kind, false, false, false);
        var ext = MediaKinds.Extension(name);
        var safe = kind switch
        {
            MediaKind.Video => WindowsSafeVideoExts.Contains(ext),
            MediaKind.Audio => WindowsSafeAudioExts.Contains(ext),
            _ => false,
        };
        return new MediaCapability(kind, hasLocalFile && safe, CanStream: true, PrefersExternal: !safe);
    }
}
