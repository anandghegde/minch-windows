namespace Minch.ViewModels;

public enum LibrarySection
{
    Active,
    Downloaded,
    Videos,
    Audio,
    Recent,
}

public static class LibrarySections
{
    public static readonly IReadOnlyList<LibrarySection> All =
        [LibrarySection.Active, LibrarySection.Downloaded, LibrarySection.Videos, LibrarySection.Audio, LibrarySection.Recent];

    public static string Title(this LibrarySection s) => s switch
    {
        LibrarySection.Active => "Active",
        LibrarySection.Downloaded => "Downloaded",
        LibrarySection.Videos => "Videos",
        LibrarySection.Audio => "Audio",
        LibrarySection.Recent => "Recent",
        _ => s.ToString(),
    };

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public static string Glyph(this LibrarySection s) => s switch
    {
        LibrarySection.Active => "",     // LightningBolt
        LibrarySection.Downloaded => "", // Download
        LibrarySection.Videos => "",     // Movies
        LibrarySection.Audio => "",      // Audio
        LibrarySection.Recent => "",     // Recent
        _ => "",
    };

    public static (string Title, string Message) EmptyState(this LibrarySection s) => s switch
    {
        LibrarySection.Active => ("Nothing in flight", "Paste a magnet above and it will appear here."),
        LibrarySection.Downloaded => ("No completed transfers", "Finished transfers will show up here once they complete in TorBox."),
        LibrarySection.Videos => ("No video files", "Video files across your transfers will land here."),
        LibrarySection.Audio => ("No audio files", "Audio files across your transfers will land here."),
        LibrarySection.Recent => ("Nothing added recently", "Transfers added in the last 7 days will appear here."),
        _ => ("Nothing here", ""),
    };

    public static string Key(this LibrarySection s) => s.ToString().ToLowerInvariant();

    public static LibrarySection? FromKey(string? key) =>
        Enum.TryParse<LibrarySection>(key, ignoreCase: true, out var s) ? s : null;
}
