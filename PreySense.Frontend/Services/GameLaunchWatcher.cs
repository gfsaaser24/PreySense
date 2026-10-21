using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private string? _activeExe;
        private int _tickRunning;

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
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            try
            {
                GameProfile? match = null;
                foreach (GameProfile profile in _getProfiles())
                {
                    if (!profile.Enabled || string.IsNullOrWhiteSpace(profile.ProcessName))
                        continue;
                    if (Process.GetProcessesByName(profile.ProcessName).Length == 0)
                        continue;
                    match = profile;
                    break;
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
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
