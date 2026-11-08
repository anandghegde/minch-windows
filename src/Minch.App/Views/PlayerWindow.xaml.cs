using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Minch.ViewModels;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;

namespace Minch.App.Views;

/// <summary>In-app player: MediaPlayerElement with TorBox HLS (AdaptiveMediaSource) or a local file.</summary>
public sealed partial class PlayerWindow : Window
{
    private readonly MediaPlayer _media = new();
    private readonly DispatcherQueueTimer _saveTimer;
    private TimeSpan? _resumeAt;
    private bool _closed;

    public PlayerWindow(PlayerViewModel vm)
    {
        Vm = vm;
        InitializeComponent();

        Title = vm.Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(PlayerTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "minch.ico"));
        AppWindow.Resize(new SizeInt32(1280, 760));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        Player.SetMediaPlayer(_media);
        _media.MediaOpened += OnMediaOpened;
        _media.MediaFailed += OnMediaFailed;

        if (vm.StartPositionSec is > 5) _resumeAt = TimeSpan.FromSeconds(vm.StartPositionSec.Value);

        vm.PropertyChanged += OnVmChanged;
        vm.SourceReplaced += (_, _) => _resumeAt = _media.PlaybackSession.Position;

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromSeconds(10);
        _saveTimer.Tick += (_, _) => SavePosition();
        _saveTimer.Start();

        Closed += OnClosed;
        Root.Loaded += async (_, _) => await vm.LoadAsync();
        if (Content is FrameworkElement root && App.MainWindow?.Content is FrameworkElement main)
            root.RequestedTheme = main.RequestedTheme;
    }

    public PlayerViewModel Vm { get; }

    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root) root.RequestedTheme = theme;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.MediaUri)) _ = SetSourceAsync();
    }

    private async Task SetSourceAsync()
    {
        if (Vm.MediaUri is not { } uri || _closed) return;
        MediaSource source;
        if (Vm.IsAdaptive)
        {
            var result = await AdaptiveMediaSource.CreateFromUriAsync(uri);
            source = result.Status == AdaptiveMediaSourceCreationStatus.Success
                ? MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource)
                : MediaSource.CreateFromUri(uri);
        }
        else
        {
            source = MediaSource.CreateFromUri(uri);
        }
        if (_closed)
        {
            source.Dispose();
            return;
        }
        var old = (_media.Source as MediaPlaybackItem)?.Source;
        _media.Source = new MediaPlaybackItem(source);
        old?.Dispose();
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        if (_resumeAt is { } at)
        {
            _resumeAt = null;
            var duration = sender.PlaybackSession.NaturalDuration;
            if (duration == TimeSpan.Zero || at < duration - TimeSpan.FromSeconds(10))
                sender.PlaybackSession.Position = at;
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = args.Error switch
        {
            MediaPlayerError.SourceNotSupported => "Windows can't play this format. Try an external player (top right).",
            MediaPlayerError.NetworkError => "Network error while streaming.",
            _ => string.IsNullOrWhiteSpace(args.ErrorMessage) ? "Playback failed." : args.ErrorMessage,
        };
        DispatcherQueue.TryEnqueue(() => Vm.Error = message);
    }

    private void SavePosition()
    {
        var s = _media.PlaybackSession;
        if (s is null) return;
        Vm.SavePosition(s.Position, s.NaturalDuration);
    }

    private void OnExternalMenuOpening(object? sender, object e)
    {
        ExternalMenu.Items.Clear();
        var players = Vm.ExternalPlayers;
        if (players.Count == 0)
        {
            ExternalMenu.Items.Add(new MenuFlyoutItem { Text = "No external players found", IsEnabled = false });
        }
        foreach (var p in players)
            ExternalMenu.Items.Add(new MenuFlyoutItem { Text = $"Open in {p.DisplayName}", Command = Vm.OpenExternalCommand, CommandParameter = p });
        if (Vm.MediaUri is { IsFile: false })
        {
            ExternalMenu.Items.Add(new MenuFlyoutSeparator());
            ExternalMenu.Items.Add(new MenuFlyoutItem { Text = "Copy stream URL", Command = Vm.CopyStreamUrlCommand });
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _saveTimer.Stop();
        SavePosition();
        Vm.PropertyChanged -= OnVmChanged;
        _media.Pause();
        Player.SetMediaPlayer(null);
        if (_media.Source is MediaPlaybackItem item) item.Source.Dispose();
        _media.Dispose();
    }
}
