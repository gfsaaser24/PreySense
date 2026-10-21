using System;
using System.IO;
using PreySense.Helpers;

namespace PreySense.Core.Acpi
{
    public class AcpiOverrideRecovery
    {
        public static bool BackupTable(string sourcePath, string backupDirectory)
        {
            try
            {
                if (!File.Exists(sourcePath)) return false;
                Directory.CreateDirectory(backupDirectory);
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(backupDirectory, fileName);
                File.Copy(sourcePath, destPath, true);
                AppLogger.Log($"[AcpiOverrideRecovery] Backed up {fileName} to {backupDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[AcpiOverrideRecovery] Backup failed: {ex.Message}");
                return false;
            }
        }

        public static bool PurgeStagedOverrides(string stagingDirectory)
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, true);
                    AppLogger.Log($"[AcpiOverrideRecovery] Purged staging directory: {stagingDirectory}");
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[AcpiOverrideRecovery] Purge failed: {ex.Message}");
                return false;
            }
        }
    }
}
