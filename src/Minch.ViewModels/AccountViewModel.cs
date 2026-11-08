using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minch.Core.Api;
using Minch.Core.Formatting;
using Minch.Core.Models;

namespace Minch.ViewModels;

public sealed record SubscriptionRow(string Title, string Detail, string Status);

/// <summary>Account page: plan, subscriptions and lifetime stats.</summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly SessionViewModel _session;

    internal AccountViewModel(SessionViewModel session) => _session = session;

    public ObservableCollection<SubscriptionRow> Subscriptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedText), nameof(UploadedText), nameof(RatioText), nameof(ItemsText), nameof(HasStats))]
    private UserStats? _stats;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _loaded;

    public bool HasStats => Stats is not null;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool HasSubscriptions => Subscriptions.Count > 0;
    public string Email => _session.User?.Email ?? "—";
    public string PlanName => _session.User?.PlanName ?? "—";
    public string SubscribedText => _session.User?.IsSubscribed switch
    {
        true => "Active subscription",
        false => "No active subscription",
        null => "",
    };

    public string DownloadedText => Stats?.TotalDownloaded is { } d ? TransferFormat.Size(d) : "—";
    public string UploadedText => Stats?.TotalUploaded is { } u ? TransferFormat.Size(u) : "—";
    public string RatioText => Stats?.Ratio is { } r ? r.ToString("0.00") : "—";
    public string ItemsText => Stats?.TotalItemsDownloaded is { } n ? n.ToString("N0") : "—";

    public static Uri ManageUrl { get; } = new("https://torbox.app/settings");
    public static Uri ApiKeyUrl { get; } = new("https://torbox.app/settings?section=api");

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!_session.IsSignedIn || IsLoading) return;
        IsLoading = true;
        Error = null;
        NotifyUser();
        try
        {
            var statsTask = _session.Client.StatsAsync();
            var subsTask = _session.Client.SubscriptionsAsync();
            UserStats? stats = null;
            IReadOnlyList<Subscription> subs = [];
            try { stats = await statsTask; } catch (Exception ex) { Error = ApiException.Friendly(ex, "Couldn't load usage."); }
            try { subs = await subsTask; } catch { /* plans without subscriptions return errors */ }
            _session.OnUi(() =>
            {
                Stats = stats;
                Subscriptions.Clear();
                foreach (var s in subs) Subscriptions.Add(ToRow(s));
                OnPropertyChanged(nameof(HasSubscriptions));
                Loaded = true;
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static SubscriptionRow ToRow(Subscription s)
    {
        var title = s.PlanName ?? s.PlanCode ?? "Subscription";
        var parts = new List<string>();
        if (s.Gateway is { Length: > 0 } g) parts.Add(char.ToUpperInvariant(g[0]) + g[1..]);
        if (s.CreatedAt is { } c) parts.Add($"since {c.ToLocalTime():d MMM yyyy}");
        return new SubscriptionRow(title, string.Join(" · ", parts), s.Status ?? "");
    }

    public void NotifyUser()
    {
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(PlanName));
        OnPropertyChanged(nameof(SubscribedText));
    }

    [RelayCommand]
    private void OpenManage() => _session.Services.Shell?.OpenUrl(ManageUrl);

    internal void Reset()
    {
        Stats = null;
        Subscriptions.Clear();
        Error = null;
        Loaded = false;
        OnPropertyChanged(nameof(HasSubscriptions));
        NotifyUser();
    }
}
