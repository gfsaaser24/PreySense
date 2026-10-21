using System;
using System.Drawing;
using System.Runtime.Versioning;

namespace PreySense
{
    /// <summary>
    /// Lid cover logo HID path. The logo is ENEK5130 target 0x83 on the same
    /// controller as the keyboard (target 0x21). The shared EC command channel
    /// must not be used here because it also writes keyboard lighting.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class AcerCoverLogoHidClient : IDisposable
    {
        private readonly EnekRgbClient _enek = new();
        private bool _isDisposed;

        public bool IsAvailable => !_isDisposed && _enek.IsAvailable;

        public bool SetLogoLighting(int mode, Color color, byte brightness, byte speed)
        {
            if (_isDisposed) return false;
            return _enek.SetLogoLighting(mode, color, brightness, speed);
        }

        public bool TurnOff()
        {
            return SetLogoLighting(0, Color.Black, 0, 1);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _enek.Dispose();
        }
    }
}
