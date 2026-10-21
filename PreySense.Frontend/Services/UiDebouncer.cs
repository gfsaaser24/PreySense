using System;
using System.Windows.Threading;

namespace PreySense.Frontend.Services;

/// <summary>
/// Coalesces rapid UI changes into one callback on the owning dispatcher.
/// Replaces the repeated create/stop/start DispatcherTimer pattern used by sliders.
/// </summary>
public sealed class UiDebouncer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Action? _pendingAction;

    public UiDebouncer(TimeSpan delay, DispatcherPriority priority = DispatcherPriority.Background)
    {
        _timer = new DispatcherTimer(priority) { Interval = delay };
        _timer.Tick += OnTick;
    }

    public void Debounce(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pendingAction = action;
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _pendingAction = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        var action = _pendingAction;
        _pendingAction = null;
        action?.Invoke();
    }

    public void Dispose()
    {
        Cancel();
        _timer.Tick -= OnTick;
    }
}
