using Minch.Core.Downloads;
using Minch.Core.Formatting;
using Minch.Core.Input;
using Minch.Core.Media;
using Minch.Core.Updates;

namespace Minch.Core.Tests;

public class ContentRangeTests
{
    [Fact]
    public void ParsesValidHeader()
    {
        var r = ContentRange.Parse("bytes 0-99/1000");
        Assert.NotNull(r);
        Assert.Equal(0, r.Value.Start);
        Assert.Equal(99, r.Value.End);
        Assert.Equal(1000, r.Value.TotalBytes);
        Assert.Equal(100, r.Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bytes */1000")]
    [InlineData("bytes 10-5/1000")]
    [InlineData("bytes 0-1000/1000")]
    [InlineData("items 0-1/2")]
    [InlineData("bytes 0-1/0")]
    public void RejectsInvalidHeader(string? value) => Assert.Null(ContentRange.Parse(value));

    [Fact]
    public void PlannerCoversEveryByteOnce()
    {
        var chunks = RangePlanner.Plan(10, 4);
        Assert.Equal(3, chunks.Count);
        Assert.Equal((0L, 3L), (chunks[0].Start, chunks[0].End));
        Assert.Equal((8L, 9L), (chunks[2].Start, chunks[2].End));
        Assert.Equal(10, chunks.Sum(c => c.Length));
    }

    [Fact]
    public void PlannerHandlesEmptyFile() => Assert.Empty(RangePlanner.Plan(0, 4));
}

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("a:b*c?.mkv", "a_b_c_.mkv")]
    [InlineData("  name. ", "name")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("", "Untitled")]
    [InlineData("???", "Untitled")]
    [InlineData("Movie (2024).mkv", "Movie (2024).mkv")]
    public void Sanitizes(string input, string expected) => Assert.Equal(expected, FileNameSanitizer.Sanitize(input));

    [Fact]
    public void TruncatesKeepingExtension()
    {
        var result = FileNameSanitizer.Sanitize(new string('x', 300) + ".mkv");
        Assert.True(result.Length <= FileNameSanitizer.MaxSegmentLength);
        Assert.EndsWith(".mkv", result);
    }
}

public class MediaCapabilityTests
{
    [Theory]
    [InlineData("a.mkv", null, MediaKind.Video)]
    [InlineData("a.FLAC", null, MediaKind.Audio)]
    [InlineData("a.bin", "video/mp4", MediaKind.Video)]
    [InlineData("readme.txt", null, MediaKind.Other)]
    public void DetectsKind(string name, string? mime, MediaKind expected) => Assert.Equal(expected, MediaKinds.Detect(name, mime));

    [Fact]
    public void LocalPlaybackNeedsLocalFileAndSafeFormat()
    {
        Assert.True(MediaCapability.Evaluate("a.mkv", null, hasLocalFile: true).CanPlayLocally);
        Assert.False(MediaCapability.Evaluate("a.mkv", null, hasLocalFile: false).CanPlayLocally);
        var vob = MediaCapability.Evaluate("a.vob", null, hasLocalFile: true);
        Assert.False(vob.CanPlayLocally);
        Assert.True(vob.PrefersExternal);
        Assert.True(vob.CanStream);
    }

    [Fact]
    public void OtherFilesAreNotPlayable()
    {
        var c = MediaCapability.Evaluate("a.zip", null, true);
        Assert.False(c.CanPlayLocally || c.CanStream || c.PrefersExternal);
    }
}

public class TransferFormatTests
{
    [Theory]
    [InlineData(0, "Zero KB")]
    [InlineData(1, "1 byte")]
    [InlineData(999, "999 bytes")]
    [InlineData(12_000, "12 KB")]
    [InlineData(1_500_000, "1.5 MB")]
    [InlineData(2_340_000_000, "2.34 GB")]
    public void Size(long bytes, string expected) => Assert.Equal(expected, TransferFormat.Size(bytes));

    [Fact]
    public void SpeedIsNullWhenIdle() => Assert.Null(TransferFormat.Speed(0));

    [Fact]
    public void SpeedUsesBinaryUnits() => Assert.Equal("2 MB/s", TransferFormat.Speed(2 * 1024 * 1024));

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.426, "43%")]
    [InlineData(1.7, "100%")]
    [InlineData(-1.0, "0%")]
    public void Percent(double p, string expected) => Assert.Equal(expected, TransferFormat.Percent(p));
}

public class AddInputTests
{
    [Theory]
    [InlineData(null, AddInputKind.Empty)]
    [InlineData("   ", AddInputKind.Empty)]
    [InlineData("magnet:?xt=urn:btih:abc", AddInputKind.Magnet)]
    [InlineData("https://example.com/file.zip", AddInputKind.WebLink)]
    [InlineData("ftp://example.com/file", AddInputKind.Invalid)]
    [InlineData("hello world", AddInputKind.Invalid)]
    public void Classifies(string? text, AddInputKind expected) => Assert.Equal(expected, AddInput.Classify(text));

    [Fact]
    public void ExtractsBtih()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        Assert.Equal(hash, AddInput.BtihHash($"magnet:?dn=x&xt=urn%3Abtih%3A{hash}&tr=udp://t"));
        Assert.Null(AddInput.BtihHash("magnet:?dn=x"));
        Assert.Null(AddInput.BtihHash("https://example.com"));
    }

    [Fact]
    public void ExternalMagnetAcceptsRawAndWrapped()
    {
        const string magnet = "magnet:?xt=urn:btih:abc&dn=Some+Name";
        Assert.Equal(magnet, AddInput.ExternalMagnet($"\"{magnet}\""));
        Assert.Equal("magnet:?xt=urn:btih:abc", AddInput.ExternalMagnet("minch://addmagnet?url=" + Uri.EscapeDataString("magnet:?xt=urn:btih:abc")));
        Assert.Null(AddInput.ExternalMagnet("minch://addmagnet?other=1"));
        Assert.Null(AddInput.ExternalMagnet(@"C:\file.torrent"));
    }

    [Fact]
    public void MatchesTrackByLanguageOrLabel()
    {
        var tracks = new[]
        {
            new Api.StreamTrack(0, "English", "en"),
            new Api.StreamTrack(1, "Français", "fre"),
            new Api.StreamTrack(2, "Japanese (Signs)", null),
        };
        Assert.Equal(0, AddInput.MatchTrack(tracks, "eng"));
        Assert.Equal(1, AddInput.MatchTrack(tracks, "fr"));
        Assert.Equal(2, AddInput.MatchTrack(tracks, "japanese"));
        Assert.Null(AddInput.MatchTrack(tracks, "de"));
        Assert.Null(AddInput.MatchTrack(tracks, null));
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("v2", "2.0.0")]
    [InlineData("v1.0.0-beta.1", "1.0.0")]
    public void ParsesTags(string tag, string expected) => Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Fact]
    public void RejectsGarbage() => Assert.Null(UpdateChecker.ParseVersion("latest"));
}
