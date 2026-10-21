using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PreySense.Core.Acpi
{
    public static class OpenCoreSecurityValidator
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFirmwareEnvironmentVariable(
            string lpName,
            string lpGuid,
            byte[] pBuffer,
            uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint EnumSystemFirmwareTables(
            uint firmwareTableProviderSignature,
            IntPtr pFirmwareTableEnumBuffer,
            uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableId,
            byte[]? pFirmwareTableBuffer,
            uint bufferSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr ProcessHandle,
            uint DesiredAccess,
            out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(
            string? lpSystemName,
            string lpName,
            out long lpLuid);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            int BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static void TryEnableSystemEnvironmentPrivilege()
        {
            try
            {
                if (OpenProcessToken(GetCurrentProcess(), 0x0020 /* TOKEN_ADJUST_PRIVILEGES */ | 0x0008 /* TOKEN_QUERY */, out IntPtr token))
                {
                    try
                    {
                        if (LookupPrivilegeValue(null, "SeSystemEnvironmentPrivilege", out long luid))
                        {
                            var tp = new TOKEN_PRIVILEGES
                            {
                                PrivilegeCount = 1,
                                Luid = luid,
                                Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                            };
                            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                    finally
                    {
                        CloseHandle(token);
                    }
                }
            }
            catch { }
        }

        public static bool DetectIsOpenCoreLoaded()
        {
            // 1. Query UEFI NVRAM opencore-version variable (GUID: {4D1FDA02-38C7-4A6A-9CC6-4BCCA8B30102})
            // Requires SeSystemEnvironmentPrivilege, which we attempt to enable in the process token.
            try
            {
                TryEnableSystemEnvironmentPrivilege();
                byte[] buf = new byte[256];
                uint read = GetFirmwareEnvironmentVariable("opencore-version", "{4D1FDA02-38C7-4A6A-9CC6-4BCCA8B30102}", buf, (uint)buf.Length);
                if (read > 0)
                {
                    return true;
                }
            }
            catch { }

            // 2. Query Windows registry ACPI table registry map (HKLM\HARDWARE\ACPI)
            // When OpenCore boots, injected tables (e.g. SSDT-PREYCPU, SSDT-PREYBST, SSDT-PREYDEV)
            // and hotpatched tables (e.g. SSDJ containing RTNEVAoN) are loaded into HKLM\HARDWARE\ACPI.
            // This registry check is unprivileged and accessible to all user processes.
            try
            {
                using var acpiKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI");
                if (acpiKey != null)
                {
                    foreach (var tableName in acpiKey.GetSubKeyNames())
                    {
                        using var tableKey = acpiKey.OpenSubKey(tableName);
                        if (tableKey == null) continue;

                        foreach (var oemId in tableKey.GetSubKeyNames())
                        {
                            if (oemId.Equals("PREY", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }

                            using var oemKey = tableKey.OpenSubKey(oemId);
                            if (oemKey == null) continue;

                            foreach (var tableId in oemKey.GetSubKeyNames())
                            {
                                if (tableId.Contains("PREY", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }

                                using var revKey = oemKey.OpenSubKey(tableId);
                                if (revKey == null) continue;

                                foreach (var rev in revKey.GetSubKeyNames())
                                {
                                    using var leafKey = revKey.OpenSubKey(rev);
                                    if (leafKey?.GetValue("00000000") is byte[] data && data.Length > 0)
                                    {
                                        string s = System.Text.Encoding.ASCII.GetString(data);
                                        if (s.Contains("RTNEVAoN") || s.Contains("PREY") || s.Contains("PBOO"))
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Query all live ACPI tables via Win32 EnumSystemFirmwareTables / GetSystemFirmwareTable
            try
            {
                uint acpiProvider = 0x41435049; // 'ACPI'
                uint size = EnumSystemFirmwareTables(acpiProvider, IntPtr.Zero, 0);
                if (size > 0)
                {
                    IntPtr pBuffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        uint bytesEnum = EnumSystemFirmwareTables(acpiProvider, pBuffer, size);
                        int count = (int)bytesEnum / 4;
                        for (int i = 0; i < count; i++)
                        {
                            uint tableId = (uint)Marshal.ReadInt32(pBuffer, i * 4);
                            uint tableSize = GetSystemFirmwareTable(acpiProvider, tableId, null, 0);
                            if (tableSize > 0)
                            {
                                byte[] tableBuf = new byte[tableSize];
                                if (GetSystemFirmwareTable(acpiProvider, tableId, tableBuf, tableSize) > 0)
                                {
                                    if (tableBuf.Length >= 24)
                                    {
                                        string oemId = System.Text.Encoding.ASCII.GetString(tableBuf, 10, 6);
                                        string oemTableId = System.Text.Encoding.ASCII.GetString(tableBuf, 16, 8);
                                        if (oemId.Contains("PREY", StringComparison.OrdinalIgnoreCase) ||
                                            oemTableId.Contains("PREY", StringComparison.OrdinalIgnoreCase))
                                        {
                                            return true;
                                        }
                                    }

                                    string tableStr = System.Text.Encoding.ASCII.GetString(tableBuf);
                                    if (tableStr.Contains("RTNEVAoN") || tableStr.Contains("PREY") || tableStr.Contains("PBOO"))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pBuffer);
                    }
                }
            }
            catch { }

            // 4. Query Windows registry cached firmware variables for OpenCore GUID
            try
            {
                using var nvramKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FirmwareEnvironmentVariables\{4d1fda02-38c7-4a6a-9cc6-4bcca8b30102}");
                if (nvramKey != null)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static string GetAcpiOverrideLabel(bool isOpenCoreLoaded) =>
            isOpenCoreLoaded ? "OpenCore Active" : "OpenCore Not Loaded";
    }
}
