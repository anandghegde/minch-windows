using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Minch.Core.Media;
using Minch.Core.Models;

namespace Minch.ViewModels;

public sealed record PaletteItem(string Title, string Subtitle, string Glyph, Func<Task> Run, int Rank = 0);

/// <summary>Ctrl+K quick switcher: app commands, sections, transfers and downloaded media.</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private const int MaxResults = 40;
    private readonly SessionViewModel _session;

    internal CommandPaletteViewModel(SessionViewModel session) => _session = session;

    public ObservableCollection<PaletteItem> Results { get; } = [];

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private PaletteItem? _selected;

    /// <summary>Raised when a command wants the add box focused (the view handles it).</summary>
    public event EventHandler? FocusAddRequested;
    /// <summary>Raised when a command navigates to a non-library page ("settings", "account").</summary>
    public event EventHandler<string>? NavigateRequested;

    partial void OnQueryChanged(string value) => Refresh();

    public void Open()
    {
        Query = "";
        Refresh();
    }

    public void Refresh()
    {
        var items = Build();
        var q = Query.Trim();
        var ranked = string.IsNullOrEmpty(q)
            ? items.Where(i => i.Rank >= 0).Take(MaxResults).ToList()
            : items
                .Select(i => (Item: i, Score: Score(i, q)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.Rank)
                .Take(MaxResults)
                .Select(x => x.Item)
                .ToList();
        Results.Clear();
        foreach (var r in ranked) Results.Add(r);
        Selected = Results.FirstOrDefault();
    }

    public async Task RunAsync(PaletteItem? item)
    {
        item ??= Selected;
        if (item is null) return;
        try { await item.Run(); }
        catch (Exception ex) { _session.ShowError(ex.Message); }
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        var i = Selected is null ? -1 : Results.IndexOf(Selected);
        i = Math.Clamp(i + delta, 0, Results.Count - 1);
        Selected = Results[i];
    }

    internal static int Score(PaletteItem item, string query)
    {
        var title = item.Title;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 100;
        var idx = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return 80 - Math.Min(idx, 40);
        if (item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)) return 30;
        // subsequence fuzzy match
        var qi = 0;
        foreach (var c in title)
        {
            if (qi < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[qi])) qi++;
        }
        return qi == query.Length ? 10 : 0;
    }

    private List<PaletteItem> Build()
    {
        var list = new List<PaletteItem>
        {
            new("Refresh", "Sync transfers with TorBox now", "", () => _session.RefreshAsync(), 50),
            new("Add magnet or link…", "Focus the add box", "", () => { FocusAddRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }, 49),
            new("Settings", "Open settings", "", () => { NavigateRequested?.Invoke(this, "settings"); return Task.CompletedTask; }, 20),
            new("Account", "Plan, usage and API key", "", () => { NavigateRequested?.Invoke(this, "account"); return Task.CompletedTask; }, 20),
            new("Sign out", "Remove the API key from this PC", "", () => _session.SignOutAsync(), -1),
        };

        foreach (var section in LibrarySections.All)
        {
            var s = section;
            list.Add(new($"Go to {s.Title()}", "Library", s.Glyph(), () =>
            {
                NavigateRequested?.Invoke(this, "library");
                _session.Library.SelectedSection = s;
                return Task.CompletedTask;
            }, 30));
        }

        foreach (var t in _session.Library.AllItems.OrderByDescending(t => t.Transfer.AddedAt))
        {
            var vm = t;
            list.Add(new(vm.Name, $"{vm.KindLabel} · {vm.StatusLabel}", vm.StatusGlyph, () =>
            {
                NavigateRequested?.Invoke(this, "library");
                var lib = _session.Library;
                if (!LibraryViewModel.Matches(vm.Transfer, lib.SelectedSection, DateTimeOffset.UtcNow))
                    lib.SelectedSection = vm.IsDone ? LibrarySection.Downloaded : LibrarySection.Active;
                lib.SearchText = "";
                vm.IsExpanded = true;
                return Task.CompletedTask;
            }, 0));

            foreach (var f in vm.Files.Where(f => f.IsDownloaded))
            {
                var file = f;
                if (file.Kind != MediaKind.Other)
                    list.Add(new($"Play {file.Name}", vm.Name, "", () => file.PlayCommand.ExecuteAsync(null), -1));
                list.Add(new($"Reveal {file.Name}", "Show in File Explorer", "", () => { file.RevealCommand.Execute(null); return Task.CompletedTask; }, -1));
            }
        }
        return list;
    }
}
