using Microsoft.UI.Dispatching;
using Minch.Core.Services;

namespace Minch.App.Services;

public sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;
    public void Post(Action action) => queue.TryEnqueue(() => action());
}
