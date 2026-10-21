using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PreySense.Helpers;

namespace PreySense.Core.Services
{
    /// <summary>
    /// Driverless Windows PDH CPU Frequency & Performance Monitor.
    /// Uses native pdh.dll P/Invoke to sample '\Processor Information(*)\% Processor Performance'.
    /// Accounts for Intel hybrid P-core/E-core architectures (such as Core Ultra 9 275HX)
    /// by weighting core nominal base clocks against their instantaneous performance percentage.
    /// </summary>
    public sealed class PdhCpuFrequencyMonitor : IDisposable
    {
        #region PDH P/Invoke

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint ERROR_SUCCESS = 0;

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll", SetLastError = true)]
        private static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll", SetLastError = true)]
        private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

        [DllImport("pdh.dll", SetLastError = true)]
        private static extern uint PdhCloseQuery(IntPtr hQuery);

        [StructLayout(LayoutKind.Explicit)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)]
            public uint CStatus;
            [FieldOffset(8)]
            public double doubleValue;
            [FieldOffset(8)]
            public long longValue;
        }

        #endregion

        private IntPtr _hQuery = IntPtr.Zero;
        private readonly List<(IntPtr CounterHandle, int CoreIndex, uint NominalMhz)> _coreCounters = new();
        private IntPtr _totalCounter = IntPtr.Zero;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private readonly uint[] _coreNominalMhz;

        public bool IsAvailable => _isInitialized && _hQuery != IntPtr.Zero;
        public int CoreCount => _coreCounters.Count;

        public PdhCpuFrequencyMonitor()
        {
            // Discover nominal per-core frequencies (P-cores vs E-cores have different base clocks)
            var ppi = ProcessorNominalInfo.GetCoreNominalFrequencies();
            _coreNominalMhz = new uint[ppi.Length];
            for (int i = 0; i < ppi.Length; i++)
            {
                _coreNominalMhz[i] = ppi[i].MaxMhz > 0 ? ppi[i].MaxMhz : 2700; // Fallback to 2700MHz if 0
            }

            InitializePdh();
        }

        private void InitializePdh()
        {
            try
            {
                uint status = PdhOpenQuery(null, IntPtr.Zero, out _hQuery);
                if (status != ERROR_SUCCESS)
                {
                    AppLogger.Log($"PdhCpuFrequencyMonitor: PdhOpenQuery failed with code 0x{status:X8}");
                    return;
                }

                // Add per-core counters: \Processor Information(0,i)\% Processor Performance
                for (int i = 0; i < _coreNominalMhz.Length; i++)
                {
                    string path = $@"\Processor Information(0,{i})\% Processor Performance";
                    uint addStatus = PdhAddEnglishCounter(_hQuery, path, IntPtr.Zero, out IntPtr hCounter);
                    if (addStatus == ERROR_SUCCESS)
                    {
                        _coreCounters.Add((hCounter, i, _coreNominalMhz[i]));
                    }
                }

                // Add _Total counter
                string totalPath = @"\Processor Information(_Total)\% Processor Performance";
                PdhAddEnglishCounter(_hQuery, totalPath, IntPtr.Zero, out _totalCounter);

                // Initial baseline sample (PDH requires at least 2 samples to compute rate deltas)
                PdhCollectQueryData(_hQuery);
                _isInitialized = _coreCounters.Count > 0;
                AppLogger.Log($"PdhCpuFrequencyMonitor: Initialized successfully with {_coreCounters.Count} physical/logical core counters.");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PdhCpuFrequencyMonitor: Initialization exception: {ex.Message}");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Collects query data and returns estimated CPU frequencies.
        /// Returns (MaxCoreMhz, AverageCoreMhz, PerCoreMhz).
        /// If PDH is unavailable, returns false.
        /// </summary>
        public bool TrySampleFrequencies(out int maxMhz, out int avgMhz, out int[] coreMhz)
        {
            maxMhz = 0;
            avgMhz = 0;
            coreMhz = Array.Empty<int>();

            if (!_isInitialized || _isDisposed || _hQuery == IntPtr.Zero)
                return false;

            try
            {
                uint collectStatus = PdhCollectQueryData(_hQuery);
                if (collectStatus != ERROR_SUCCESS)
                    return false;

                coreMhz = new int[_coreCounters.Count];
                double totalMhz = 0;
                int max = 0;

                for (int i = 0; i < _coreCounters.Count; i++)
                {
                    var (hCounter, coreIndex, nominalMhz) = _coreCounters[i];
                    uint valStatus = PdhGetFormattedCounterValue(hCounter, PDH_FMT_DOUBLE, out _, out var val);

                    if (valStatus == ERROR_SUCCESS && val.CStatus == ERROR_SUCCESS)
                    {
                        // Frequency (MHz) ≈ Nominal MHz * (Performance % / 100)
                        double perfPercent = val.doubleValue;
                        int mhz = (int)Math.Round(nominalMhz * (perfPercent / 100.0));
                        coreMhz[i] = mhz;
                        if (mhz > max) max = mhz;
                        totalMhz += mhz;
                    }
                    else
                    {
                        coreMhz[i] = (int)nominalMhz;
                    }
                }

                maxMhz = max;
                avgMhz = _coreCounters.Count > 0 ? (int)Math.Round(totalMhz / _coreCounters.Count) : 0;
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PdhCpuFrequencyMonitor: Sample error: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_hQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_hQuery);
                _hQuery = IntPtr.Zero;
            }
            _coreCounters.Clear();
        }
    }
}
