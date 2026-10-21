using System;
using PreySense;

namespace PreySense.Gpu
{
    public class NvidiaWrapper
    {
        /// <summary>
        /// Sets the GPU display mode via native NVIDIA Advanced Optimus (DDS).
        /// 0 = Optimus (iGPU), 1 = Automatic, 2 = NVIDIA GPU Only (dGPU).
        /// </summary>
        public static bool SetGpuMode(int mode)
        {
            var ddsMode = mode switch
            {
                2 => DdsDisplayMode.NvidiaGpuOnly,
                1 => DdsDisplayMode.Automatic,
                _ => DdsDisplayMode.Optimus
            };

            if (NvidiaDdsService.Instance.IsAvailable && NvidiaDdsService.Instance.IsDdsSupported())
            {
                var (ok, msg) = NvidiaDdsService.Instance.SetMode(ddsMode);
                AppLogger.Log($"NvidiaWrapper.SetGpuMode({mode}): {msg}");
                return ok;
            }

            AppLogger.Log($"NvidiaWrapper.SetGpuMode({mode}): NVIDIA DDS is not available or supported.");
            return false;
        }
    }
}
