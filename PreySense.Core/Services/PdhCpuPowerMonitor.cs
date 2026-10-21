using System;
using System.Runtime.InteropServices;
using PreySense.Helpers;

namespace PreySense.Core.Services
{
    /// <summary>
    /// Read-only monitor for Intel CPU RAPL package power consumption via Windows Energy Meter native PDH counters.
    /// Operates 100% driverless using pdh.dll without requiring external NuGet packages or kernel drivers.
    /// </summary>
    public sealed class PdhCpuPowerMonitor : IDisposable
    {
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

        private IntPtr _hQuery = IntPtr.Zero;
        private IntPtr _hCounter = IntPtr.Zero;
        private bool _isInitialized = false;
        private bool _isDisposed = false;

        public bool IsAvailable => _isInitialized && _hCounter != IntPtr.Zero;

        public PdhCpuPowerMonitor()
        {
            InitializeCounter();
        }

        private void InitializeCounter()
        {
            try
            {
                uint status = PdhOpenQuery(null, IntPtr.Zero, out _hQuery);
                if (status != ERROR_SUCCESS) return;

                // Counter: \Energy Meter(rapl_package0_pkg)\Power
                // Values reported in milliwatts
                status = PdhAddEnglishCounter(_hQuery, @"\Energy Meter(rapl_package0_pkg)\Power", IntPtr.Zero, out _hCounter);
                if (status == ERROR_SUCCESS)
                {
                    // Prime initial sample
                    PdhCollectQueryData(_hQuery);
                    _isInitialized = true;
                    AppLogger.Log("PdhCpuPowerMonitor: Initialized Energy Meter RAPL package counter via pdh.dll.");
                }
                else
                {
                    PdhCloseQuery(_hQuery);
                    _hQuery = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PdhCpuPowerMonitor: Initialization error: {ex.Message}");
            }
        }

        /// <summary>
        /// Samples instantaneous CPU package power in Watts.
        /// Returns 0 if counter is unavailable or unprimed.
        /// </summary>
        public float GetPackagePowerWatts()
        {
            if (!_isInitialized || _isDisposed || _hQuery == IntPtr.Zero || _hCounter == IntPtr.Zero)
                return 0f;

            try
            {
                uint status = PdhCollectQueryData(_hQuery);
                if (status != ERROR_SUCCESS) return 0f;

                status = PdhGetFormattedCounterValue(_hCounter, PDH_FMT_DOUBLE, out _, out var val);
                if (status == ERROR_SUCCESS && val.CStatus == ERROR_SUCCESS)
                {
                    // Convert mW to Watts
                    return (float)Math.Round(val.doubleValue / 1000.0, 1);
                }
                return 0f;
            }
            catch
            {
                return 0f;
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
            _hCounter = IntPtr.Zero;
        }
    }
}
