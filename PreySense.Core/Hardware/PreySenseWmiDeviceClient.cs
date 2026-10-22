using System;
using System.Management;

namespace PreySense.Core.Hardware
{
    public class PreySenseWmiDeviceClient
    {
        public const string WmiNamespace = @"root\WMI";
        public const string TargetGuid = "09750C8E-7D05-406A-A517-E3EE0DDB63FF";
        public const string WmiClassName = "PreySenseControl";

        public readonly record struct PowerSnapshot(
            bool Success,
            uint Pl1Milliwatts,
            uint Pl2Milliwatts,
            byte? TauEncoded,
            uint GpuTargetWatts,
            uint GpuTripCelsius);

        /// <summary>
        /// Reads all launch-time power values through one WMI lookup. ACPI method
        /// calls remain sequential because firmware serializes access internally.
        /// </summary>
        public static PowerSnapshot GetPowerSnapshot()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    uint pl1 = 0;
                    uint pl2 = 0;
                    byte? tau = null;
                    uint gpuWatts = 0;
                    uint gpuTrip = 0;
                    bool anySuccess = false;

                    using (var input = obj.GetMethodParameters("GetCpuPowerLimits"))
                    using (var output = obj.InvokeMethod("GetCpuPowerLimits", input, null))
                    {
                        if (Convert.ToUInt32(output["ReturnCode"]) == 0)
                        {
                            pl1 = Convert.ToUInt32(output["Pl1Milliwatts"]);
                            pl2 = Convert.ToUInt32(output["Pl2Milliwatts"]);
                            foreach (PropertyData property in output.Properties)
                            {
                                if (property.Name.Equals("TauTimeWindow", StringComparison.OrdinalIgnoreCase) && property.Value != null)
                                {
                                    tau = (byte)(Convert.ToUInt32(property.Value) & 0x7F);
                                    break;
                                }
                            }
                            anySuccess = true;
                        }
                    }

                    using (var input = obj.GetMethodParameters("GetGpuPowerLimit"))
                    using (var output = obj.InvokeMethod("GetGpuPowerLimit", input, null))
                    {
                        if (Convert.ToUInt32(output["ReturnCode"]) == 0)
                        {
                            gpuWatts = Convert.ToUInt32(output["TargetWatts"]);
                            anySuccess = true;
                        }
                    }

                    using (var input = obj.GetMethodParameters("GetGpuThermalTripPoint"))
                    using (var output = obj.InvokeMethod("GetGpuThermalTripPoint", input, null))
                    {
                        if (Convert.ToUInt32(output["ReturnCode"]) == 0)
                        {
                            gpuTrip = Convert.ToUInt32(output["TempCelsius"]);
                            anySuccess = true;
                        }
                    }

                    return new PowerSnapshot(anySuccess, pl1, pl2, tau, gpuWatts, gpuTrip);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] GetPowerSnapshot failed: {ex.Message}");
            }

            return default;
        }

        /// <summary>
        /// Probes the dedicated PreySense ACPI-WMI Device.
        /// Method 1: GetVersion
        /// Method 2: Ping
        /// </summary>
        public static (bool found, uint version, string status) ProbeDevice()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("GetVersion");
                    using var outParams = obj.InvokeMethod("GetVersion", inParams, null);
                    uint ver = Convert.ToUInt32(outParams["Version"]);

                    using var inPing = obj.GetMethodParameters("Ping");
                    using var outPing = obj.InvokeMethod("Ping", inPing, null);
                    uint pingVal = Convert.ToUInt32(outPing["Magic"]);

                    string magic = pingVal == 0x50524559 ? "PREY (Verified)" : $"0x{pingVal:X8}";
                    return (true, ver, $"Discovered PreySense ACPI-WMI Device: Version=0x{ver:X4}, Magic={magic}");
                }

                return (false, 0, "PreySenseControl instance not found in root\\WMI namespace.");
            }
            catch (ManagementException mex)
            {
                return (false, 0, $"WMI Query Error: {mex.Message} (Code: {mex.ErrorCode})");
            }
            catch (Exception ex)
            {
                return (false, 0, $"Probe exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets CPU PL1 (Sustained) and PL2 (Burst) power limits directly via Intel MCHBAR registers.
        /// </summary>
        /// <param name="pl1Milliwatts">Sustained power limit in mW (e.g., 55000 for 55W)</param>
        /// <param name="pl2Milliwatts">Burst power limit in mW (e.g., 115000 for 115W)</param>
        public static bool SetCpuPowerLimits(uint pl1Milliwatts, uint pl2Milliwatts, byte tauTimeWindow)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("SetCpuPowerLimits");
                    inParams["Pl1Milliwatts"] = pl1Milliwatts;
                    inParams["Pl2Milliwatts"] = pl2Milliwatts;
                    foreach (PropertyData property in inParams.Properties)
                    {
                        if (property.Name == "TauTimeWindow")
                        {
                            inParams["TauTimeWindow"] = (uint)tauTimeWindow;
                            break;
                        }
                    }

                    using var outParams = obj.InvokeMethod("SetCpuPowerLimits", inParams, null);
                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    return retCode == 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] SetCpuPowerLimits failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Reads current CPU PL1 and PL2 power limits directly from Intel MCHBAR registers.
        /// </summary>
        public static (bool success, uint pl1Milliwatts, uint pl2Milliwatts) GetCpuPowerLimits()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("GetCpuPowerLimits");
                    using var outParams = obj.InvokeMethod("GetCpuPowerLimits", inParams, null);

                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    if (retCode == 0)
                    {
                        uint pl1 = Convert.ToUInt32(outParams["Pl1Milliwatts"]);
                        uint pl2 = Convert.ToUInt32(outParams["Pl2Milliwatts"]);
                        return (true, pl1, pl2);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] GetCpuPowerLimits failed: {ex.Message}");
            }

            return (false, 0, 0);
        }

        /// <summary>
        /// Reads the active CPU limits and, when exposed by the installed PREYDEV
        /// revision, the encoded RAPL Tau time window. Older revisions omit Tau.
        /// </summary>
        public static (bool success, uint pl1Milliwatts, uint pl2Milliwatts, byte? tauEncoded) GetCpuPowerLimitsWithTau()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("GetCpuPowerLimits");
                    using var outParams = obj.InvokeMethod("GetCpuPowerLimits", inParams, null);
                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    if (retCode != 0)
                        continue;

                    byte? tau = null;
                    foreach (PropertyData property in outParams.Properties)
                    {
                        if (property.Name.Equals("TauTimeWindow", StringComparison.OrdinalIgnoreCase) && property.Value != null)
                        {
                            tau = (byte)(Convert.ToUInt32(property.Value) & 0x7F);
                            break;
                        }
                    }

                    return (true,
                        Convert.ToUInt32(outParams["Pl1Milliwatts"]),
                        Convert.ToUInt32(outParams["Pl2Milliwatts"]),
                        tau);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] GetCpuPowerLimitsWithTau failed: {ex.Message}");
            }

            return (false, 0, 0, null);
        }

        /// <summary>
        /// Sets GPU target power limit dynamically via dedicated ACPI-WMI interface.
        /// Writes to active NVIDIA NPCF registers (ACBT, ATPP) and ERM4 table, then notifies nvpcf.sys (0xC0).
        /// Takes effect immediately in software with zero reboot required.
        /// </summary>
        /// <param name="targetWatts">Target wattage (e.g. 130, 140, 150)</param>
        public static bool SetGpuPowerLimit(uint targetWatts)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("SetGpuPowerLimit");
                    inParams["TargetWatts"] = targetWatts;

                    using var outParams = obj.InvokeMethod("SetGpuPowerLimit", inParams, null);
                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    return retCode == 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] SetGpuPowerLimit failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Reads current GPU target power limit directly from ACPI NPCF registers via ACPI-WMI interface.
        /// </summary>
        public static (bool success, uint targetWatts) GetGpuPowerLimit()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("GetGpuPowerLimit");
                    using var outParams = obj.InvokeMethod("GetGpuPowerLimit", inParams, null);

                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    if (retCode == 0)
                    {
                        uint watts = Convert.ToUInt32(outParams["TargetWatts"]);
                        return (true, watts);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] GetGpuPowerLimit failed: {ex.Message}");
            }

            return (false, 0);
        }

        /// <summary>
        /// Sets GPU thermal trip point (WMG3) dynamically via dedicated ACPI-WMI interface.
        /// Writes to active NVIDIA NPCF register (\_SB.NPCF.WMG3) and notifies nvpcf.sys (0xC0).
        /// Controls the critical inflection point at which Dynamic Boost power is down-throttled.
        /// Range: 70°C to 89°C (NVML GPU_MAX operating thermal limit).
        /// </summary>
        /// <param name="tempCelsius">Target trip temperature in degrees Celsius (e.g. 79, 85, 89)</param>
        public static bool SetGpuThermalTripPoint(uint tempCelsius)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("SetGpuThermalTripPoint");
                    inParams["TempCelsius"] = tempCelsius;

                    using var outParams = obj.InvokeMethod("SetGpuThermalTripPoint", inParams, null);
                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    return retCode == 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] SetGpuThermalTripPoint failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Reads current GPU thermal trip point (WMG3) directly from ACPI NPCF registers via ACPI-WMI interface.
        /// </summary>
        public static (bool success, uint tempCelsius) GetGpuThermalTripPoint()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClassName}");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    using var inParams = obj.GetMethodParameters("GetGpuThermalTripPoint");
                    using var outParams = obj.InvokeMethod("GetGpuThermalTripPoint", inParams, null);

                    uint retCode = Convert.ToUInt32(outParams["ReturnCode"]);
                    if (retCode == 0)
                    {
                        uint temp = Convert.ToUInt32(outParams["TempCelsius"]);
                        return (true, temp);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreySenseWmiDeviceClient] GetGpuThermalTripPoint failed: {ex.Message}");
            }

            return (false, 0);
        }
    }
}
