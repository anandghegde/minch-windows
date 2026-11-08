namespace Minch.Core.Api;

/// <summary>A selectable subtitle / audio / resolution variant of a TorBox stream.</summary>
public sealed record StreamTrack(int Index, string Label, string? Language = null);

/// <summary>Indices passed to /stream/createstream as chosen_*_index.</summary>
public sealed record StreamOptions(
    int? SubtitleIndex = null,
    int? AudioIndex = null,
    int? ResolutionIndex = null,
    bool? ScrobblingEnabled = null)
{
    public static readonly StreamOptions Default = new(AudioIndex: 0);
}

/// <summary>
/// Result of opening a TorBox stream: the playable HLS URL plus any track
/// metadata TorBox returned. Every track list may be empty — a minimal
/// <c>{"hls_url": …}</c> response is still a valid session.
/// </summary>
public sealed record StreamSession
{
    public required Uri Url { get; init; }
    public string? Token { get; init; }
    public IReadOnlyList<StreamTrack> Subtitles { get; init; } = [];
    public IReadOnlyList<StreamTrack> AudioTracks { get; init; } = [];
    public IReadOnlyList<StreamTrack> Resolutions { get; init; } = [];
    public int? SelectedSubtitleIndex { get; init; }
    public int? SelectedAudioIndex { get; init; }
    public int? SelectedResolutionIndex { get; init; }

    public bool HasSelectableTracks =>
        Subtitles.Count > 0 || AudioTracks.Count > 1 || Resolutions.Count > 1;
}
