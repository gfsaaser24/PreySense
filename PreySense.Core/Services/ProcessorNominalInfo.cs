using System;
using System.Runtime.InteropServices;

namespace PreySense.Core.Services
{
    public static class ProcessorNominalInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern int CallNtPowerInformation(
            int InformationLevel,
            IntPtr lpInputBuffer,
            int nInputBufferSize,
            IntPtr lpOutputBuffer,
            int nOutputBufferSize
        );

        public static PROCESSOR_POWER_INFORMATION[] GetCoreNominalFrequencies()
        {
            int count = Environment.ProcessorCount;
            int structSize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            int bufferSize = structSize * count;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

            try
            {
                int status = CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, bufferSize);
                if (status != 0)
                {
                    return Array.Empty<PROCESSOR_POWER_INFORMATION>();
                }

                var result = new PROCESSOR_POWER_INFORMATION[count];
                for (int i = 0; i < count; i++)
                {
                    IntPtr corePtr = IntPtr.Add(buffer, i * structSize);
                    result[i] = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(corePtr);
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
