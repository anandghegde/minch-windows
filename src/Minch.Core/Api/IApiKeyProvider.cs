namespace Minch.Core.Api;

public interface IApiKeyProvider
{
    ValueTask<string?> CurrentKeyAsync(CancellationToken ct = default);
}

public sealed class StaticApiKeyProvider(string? key) : IApiKeyProvider
{
    public ValueTask<string?> CurrentKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(key);
}

/// <summary>Reads a mutable key — lets the session swap keys without rebuilding the client.</summary>
public sealed class MutableApiKeyProvider : IApiKeyProvider
{
    private volatile string? _key;
    public MutableApiKeyProvider(string? key = null) => _key = key;
    public string? Key { get => _key; set => _key = value; }
    public ValueTask<string?> CurrentKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(_key);
}
