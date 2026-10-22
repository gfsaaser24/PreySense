using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using PreySense.Mode;

namespace PreySense.Frontend.Services
{
    public sealed class GameLaunchWatcher : IDisposable
    {
        private readonly Func<IReadOnlyList<GameProfile>> _getProfiles;
        private readonly Func<GameProfile, Task> _onLaunch;
        private readonly Func<Task> _onExit;
        private readonly System.Timers.Timer _timer;
        private readonly object _tickLock = new();
        private ManagementEventWatcher? _processStartWatcher;
        private ManagementEventWatcher? _processStopWatcher;
        private string? _activeExe;
        private int _tickRunning;
        private int _disposed;

        public GameLaunchWatcher(
            Func<IReadOnlyList<GameProfile>> getProfiles,
            Func<GameProfile, Task> onLaunch,
            Func<Task> onExit)
        {
            _getProfiles = getProfiles;
            _onLaunch = onLaunch;
            _onExit = onExit;
            _timer = new System.Timers.Timer(2000) { AutoReset = true };
            _timer.Elapsed += (_, _) => Tick();
            _timer.Start();
            TryStartProcessWatchers();
        }

        private void TryStartProcessWatchers()
        {
            try
            {
                _processStartWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStartTrace"));
                _processStopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStopTrace"));
                _processStartWatcher.EventArrived += ProcessEventArrived;
                _processStopWatcher.EventArrived += ProcessEventArrived;
                _processStartWatcher.Start();
                _processStopWatcher.Start();

                // Events provide immediate detection. Keep a slow poll to recover
                // from sleep/resume or a dropped WMI notification.
                _timer.Interval = 15000;
            }
            catch
            {
                StopProcessWatchers();
                _timer.Interval = 2000;
            }
        }

        private void ProcessEventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                string eventName = e.NewEvent["ProcessName"]?.ToString() ?? string.Empty;
                if (eventName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    eventName = eventName[..^4];

                foreach (GameProfile profile in _getProfiles())
                {
                    if (profile.Enabled && string.Equals(profile.ProcessName, eventName, StringComparison.OrdinalIgnoreCase))
                    {
                        Tick();
                        return;
                    }
                }
            }
            catch { }
        }

        private void StopProcessWatchers()
        {
            foreach (ManagementEventWatcher? watcher in new[] { _processStartWatcher, _processStopWatcher })
            {
                if (watcher == null) continue;
                try { watcher.Stop(); } catch { }
                watcher.Dispose();
            }
            _processStartWatcher = null;
            _processStopWatcher = null;
        }

        private void Tick()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            try
            {
                GameProfile? match = null;
                List<KeyValuePair<string, GameProfile>>? watchedProfiles = null;
                foreach (GameProfile profile in _getProfiles())
                {
                    string processName = profile.ProcessName;
                    if (profile.Enabled && !string.IsNullOrWhiteSpace(processName))
                    {
                        watchedProfiles ??= new List<KeyValuePair<string, GameProfile>>(4);
                        watchedProfiles.Add(new KeyValuePair<string, GameProfile>(processName, profile));
                    }
                }

                // Take one process snapshot per tick. Calling GetProcessesByName for
                // every profile repeatedly walks the system process table and also
                // leaves the returned Process wrappers to finalization.
                if (watchedProfiles is { Count: > 0 })
                {
                    var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Process process in Process.GetProcesses())
                    {
                        using (process)
                        {
                            try
                            {
                                runningNames.Add(process.ProcessName);
                            }
                            catch
                            {
                                // Processes can exit (or become inaccessible) while
                                // the snapshot is being inspected.
                            }
                        }
                    }

                    foreach (KeyValuePair<string, GameProfile> watched in watchedProfiles)
                    {
                        if (runningNames.Contains(watched.Key))
                        {
                            match = watched.Value;
                            break;
                        }
                    }
                }

                string? nextExe = match?.ExeName;
                bool changed;
                lock (_tickLock)
                {
                    changed = !string.Equals(_activeExe, nextExe, StringComparison.OrdinalIgnoreCase);
                    if (changed)
                        _activeExe = nextExe;
                }

                if (!changed)
                    return;

                if (match != null)
                    _ = _onLaunch(match);
                else
                    _ = _onExit();
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _tickRunning, 0);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _timer.Stop();
            _timer.Dispose();
            StopProcessWatchers();
        }
    }
}
