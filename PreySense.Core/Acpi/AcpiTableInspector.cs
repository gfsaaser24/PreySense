using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PreySense.Helpers;

namespace PreySense.Core.Acpi
{
    public class AcpiTableInspector
    {
        public const string TargetDsdpOemTableId = "ACRPRDCT";
        public const string TargetOemId = "ACRSYS";

        public class TableInfo
        {
            public bool IsValid { get; set; }
            public string Signature { get; set; } = string.Empty;
            public uint Length { get; set; }
            public byte Revision { get; set; }
            public string OemId { get; set; } = string.Empty;
            public string OemTableId { get; set; } = string.Empty;
            public string Sha256Hash { get; set; } = string.Empty;
        }

        public static TableInfo InspectTableFile(string binFilePath)
        {
            var info = new TableInfo();
            if (!File.Exists(binFilePath))
            {
                AppLogger.Log($"[AcpiTableInspector] File not found: {binFilePath}");
                return info;
            }

            try
            {
                byte[] data = File.ReadAllBytes(binFilePath);
                if (data.Length < 36) return info;

                info.Signature = Encoding.ASCII.GetString(data, 0, 4);
                info.Length = BitConverter.ToUInt32(data, 4);
                info.Revision = data[8];
                info.OemId = Encoding.ASCII.GetString(data, 10, 6).TrimEnd('\0', ' ');
                info.OemTableId = Encoding.ASCII.GetString(data, 16, 8).TrimEnd('\0', ' ');

                using var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(data);
                info.Sha256Hash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                info.IsValid = true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[AcpiTableInspector] Error inspecting {binFilePath}: {ex.Message}");
            }

            return info;
        }
    }
}
