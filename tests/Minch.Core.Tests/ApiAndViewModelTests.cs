using System.Net;
using System.Text;
using Minch.Core.Api;
using Minch.Core.Models;
using Minch.ViewModels;

namespace Minch.Core.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class FixedKey(string? key) : IApiKeyProvider
{
    public ValueTask<string?> CurrentKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(key);
}

public class TorBoxClientTests
{
    private static (TorBoxClient, FakeHandler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "secret")
    {
        var handler = new FakeHandler(respond);
        return (new TorBoxClient(new HttpClient(handler), new FixedKey(key)), handler);
    }

    [Fact]
    public async Task ListsTorrentsAndSendsBearer()
    {
        var (client, handler) = Create(_ => FakeHandler.Json("""
            {"success":true,"detail":"ok","data":[
              {"id":42,"hash":"abc","name":"Big Movie","size":1000,"download_state":"downloading","progress":0.5,
               "files":[{"id":0,"name":"Big Movie/movie.mkv","short_name":"movie.mkv","size":1000}]}
            ]}
            """));

        var list = await client.ListTransfersAsync();

        var t = Assert.Single(list);
        Assert.Equal("42", t.Id);
        Assert.Equal(TransferStatus.Downloading, t.Status);
        Assert.Equal(0.5, t.Progress);
        Assert.Equal("42:0", Assert.Single(t.Files).Id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
        Assert.Equal("secret", req.Headers.Authorization?.Parameter);
        Assert.EndsWith("/torrents/mylist", req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PrefixesWebDownloadIds()
    {
        var (client, _) = Create(_ => FakeHandler.Json("""{"success":true,"data":[{"id":7,"name":"file.zip","download_state":"completed"}]}"""));
        var t = Assert.Single(await client.ListWebDownloadsAsync());
        Assert.Equal(TorBoxIds.WebDlPrefix + "7", t.Id);
        Assert.Equal(TransferKind.WebDl, t.Kind);
        Assert.Equal(TransferStatus.Done, t.Status);
    }

    [Fact]
    public async Task MissingListDataIsEmpty()
    {
        var (client, _) = Create(_ => FakeHandler.Json("""{"success":true,"data":null}"""));
        Assert.Empty(await client.ListTransfersAsync());
    }

    [Fact]
    public async Task UnauthorizedMapsToAuth()
    {
        var (client, _) = Create(_ => FakeHandler.Json("""{"success":false,"detail":"Bad key"}""", HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.MeAsync());
        Assert.Equal(ApiErrorKind.Auth, ex.Kind);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task EnvelopeFailureMapsToValidation()
    {
        var (client, _) = Create(_ => FakeHandler.Json("""{"success":false,"detail":"Invalid magnet"}"""));
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.MeAsync());
        Assert.Equal(ApiErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task NonJsonBodyMapsToDecoding()
    {
        var (client, _) = Create(_ => FakeHandler.Json("<html>"));
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.MeAsync());
        Assert.Equal(ApiErrorKind.Decoding, ex.Kind);
    }

    [Fact]
    public async Task NetworkFailureMapsToNetwork()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("offline"));
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.MeAsync());
        Assert.Equal(ApiErrorKind.Network, ex.Kind);
    }

    [Theory]
    [InlineData("downloading", TransferStatus.Downloading)]
    [InlineData("metaDL", TransferStatus.Downloading)]
    [InlineData("seeding", TransferStatus.Seeding)]
    [InlineData("cached", TransferStatus.Done)]
    [InlineData("paused", TransferStatus.Paused)]
    [InlineData("missingFiles", TransferStatus.Error)]
    [InlineData(null, TransferStatus.Queued)]
    [InlineData("something new", TransferStatus.Queued)]
    public void MapsStatus(string? raw, TransferStatus expected) => Assert.Equal(expected, TorBoxMapping.MapStatus(raw));
}

public class LibraryFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static Transfer Make(TransferStatus status, string fileName = "a.mkv", int ageDays = 1, params string[] tags) => new()
    {
        Id = "1",
        Name = "Ubuntu ISO pack",
        Status = status,
        AddedAt = Now.AddDays(-ageDays),
        TagNames = tags,
        Files = [new TransferFile { Id = "1:0", Name = fileName }],
    };

    [Fact]
    public void SectionsFilterByStatusAndKind()
    {
        var video = Make(TransferStatus.Done, "a.mkv");
        var audio = Make(TransferStatus.Downloading, "b.flac");
        Assert.True(LibraryViewModel.Matches(video, LibrarySection.Downloaded, Now));
        Assert.False(LibraryViewModel.Matches(video, LibrarySection.Active, Now));
        Assert.True(LibraryViewModel.Matches(audio, LibrarySection.Active, Now));
        Assert.True(LibraryViewModel.Matches(video, LibrarySection.Videos, Now));
        Assert.False(LibraryViewModel.Matches(video, LibrarySection.Audio, Now));
        Assert.True(LibraryViewModel.Matches(audio, LibrarySection.Audio, Now));
    }

    [Fact]
    public void RecentIsSevenDays()
    {
        Assert.True(LibraryViewModel.Matches(Make(TransferStatus.Done, ageDays: 6), LibrarySection.Recent, Now));
        Assert.False(LibraryViewModel.Matches(Make(TransferStatus.Done, ageDays: 8), LibrarySection.Recent, Now));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ubuntu", true)]
    [InlineData("ubuntu pack", true)]
    [InlineData("ubuntu fedora", false)]
    [InlineData("linux", true)]
    [InlineData("disc1", true)]
    public void SearchRequiresEveryTerm(string query, bool expected)
    {
        var t = Make(TransferStatus.Done, "disc1.iso", 1, "linux");
        Assert.Equal(expected, LibraryViewModel.MatchesSearch(t, query));
    }
}

public class PaletteScoreTests
{
    private static PaletteItem Item(string title, string subtitle = "") => new(title, subtitle, "", () => Task.CompletedTask, 0);

    [Fact]
    public void PrefixBeatsInfixBeatsSubtitleBeatsFuzzy()
    {
        var prefix = CommandPaletteViewModel.Score(Item("Settings"), "set");
        var infix = CommandPaletteViewModel.Score(Item("Open settings"), "set");
        var subtitle = CommandPaletteViewModel.Score(Item("Preferences", "settings page"), "set");
        var fuzzy = CommandPaletteViewModel.Score(Item("Sign out"), "sgo");
        Assert.True(prefix > infix && infix > subtitle && subtitle > fuzzy && fuzzy > 0);
        Assert.Equal(0, CommandPaletteViewModel.Score(Item("Refresh"), "xyz"));
    }
}

public class AccountRowTests
{
    [Fact]
    public void BuildsSubscriptionRow()
    {
        var row = AccountViewModel.ToRow(new Subscription("sub_1", "Pro", "pro", "active", "stripe", null, null));
        Assert.Equal("Pro", row.Title);
        Assert.Equal("Stripe", row.Detail);
        Assert.Equal("active", row.Status);
    }

    [Fact]
    public void FallsBackToPlanCode()
    {
        var row = AccountViewModel.ToRow(new Subscription("sub_1", null, "essential", null, null, null, null));
        Assert.Equal("essential", row.Title);
        Assert.Equal("", row.Detail);
    }
}
