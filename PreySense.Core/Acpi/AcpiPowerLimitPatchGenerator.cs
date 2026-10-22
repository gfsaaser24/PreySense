using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using PreySense.Helpers;

namespace PreySense.Core.Acpi
{
    public class AcpiPowerLimitPatchGenerator
    {
        public class GenerationResult
        {
            public bool Success { get; set; }
            public string DslPath { get; set; } = string.Empty;
            public string AmlPath { get; set; } = string.Empty;
            public string LogOutput { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>
        /// Generates SSDT-PREYBOOST override injecting custom PL1/PL2 calculations directly into the ACPI power management pipeline.
        /// </summary>
        public static GenerationResult GenerateSsdtOverride(
            CpuPowerLimitConfiguration config,
            string outputDirectory,
            string iaslPath)
        {
            var res = new GenerationResult();
            if (!config.Validate(out string validationError))
            {
                res.ErrorMessage = validationError;
                return res;
            }

            Directory.CreateDirectory(outputDirectory);
            string dslFilePath = Path.Combine(outputDirectory, "SSDT-PREYBOOST.dsl");
            string amlFilePath = Path.Combine(outputDirectory, "SSDT-PREYBOOST.aml");

            // Convert wattage (W) to MCHBAR raw units (W * 8)
            int qPl1Raw = config.QuietPl1 * 8;
            int qPl2Raw = config.QuietPl2 * 8;
            int ePl1Raw = config.EcoPl1 * 8;
            int ePl2Raw = config.EcoPl2 * 8;
            int bPl1Raw = config.BalancedPl1 * 8;
            int bPl2Raw = config.BalancedPl2 * 8;
            int pPl1Raw = config.PerformancePl1 * 8;
            int pPl2Raw = config.PerformancePl2 * 8;
            int tPl1Raw = config.TurboPl1 * 8;
            int tPl2Raw = config.TurboPl2 * 8;

            var sb = new StringBuilder();
            sb.AppendLine("/*");
            sb.AppendLine(" * PreySense SSDT-PREYBOOST Override Table");
            sb.AppendLine(" * Target: Acer Predator PHN16S-71-98RF (Core Ultra 9 275HX)");
            sb.AppendLine(" * Generated: " + DateTime.UtcNow.ToString("o"));
            sb.AppendLine(" */");
            sb.AppendLine("DefinitionBlock (\"SSDT-PREYBOOST.aml\", \"SSDT\", 2, \"ACRSYS\", \"PREYBOST\", 0x00000001)");
            sb.AppendLine("{");
            sb.AppendLine("    External (\\_SB.TUGM, FieldUnitObj)");
            sb.AppendLine("    External (\\PPL1, FieldUnitObj)");
            sb.AppendLine("    External (\\PPL2, FieldUnitObj)");
            sb.AppendLine("    External (\\PL1E, FieldUnitObj)");
            sb.AppendLine("    External (\\PL2E, FieldUnitObj)");
            sb.AppendLine("    External (\\_TZ.TZ01, DeviceObj)");
            sb.AppendLine();
            sb.AppendLine("    Scope (\\_SB)");
            sb.AppendLine("    {");
            sb.AppendLine("        Method (PSTP, 0, Serialized)");
            sb.AppendLine("        {");
            sb.AppendLine("            Switch (ToInteger (\\_SB.TUGM))");
            sb.AppendLine("            {");
            sb.AppendLine("                Case (Zero) // Turbo Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\PPL1 = 0x{tPl1Raw:X4} // {config.TurboPl1}W");
            sb.AppendLine($"                    \\PPL2 = 0x{tPl2Raw:X4} // {config.TurboPl2}W");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (One) // Performance Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\PPL1 = 0x{pPl1Raw:X4} // {config.PerformancePl1}W");
            sb.AppendLine($"                    \\PPL2 = 0x{pPl2Raw:X4} // {config.PerformancePl2}W");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (0x02) // Balanced Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\PPL1 = 0x{bPl1Raw:X4} // {config.BalancedPl1}W");
            sb.AppendLine($"                    \\PPL2 = 0x{bPl2Raw:X4} // {config.BalancedPl2}W");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (0x03) // Quiet Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\PPL1 = 0x{qPl1Raw:X4} // {config.QuietPl1}W");
            sb.AppendLine($"                    \\PPL2 = 0x{qPl2Raw:X4} // {config.QuietPl2}W");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (0x04) // Eco Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\PPL1 = 0x{ePl1Raw:X4} // {config.EcoPl1}W");
            sb.AppendLine($"                    \\PPL2 = 0x{ePl2Raw:X4} // {config.EcoPl2}W");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            \\PL1E = One");
            sb.AppendLine("            \\PL2E = One");
            sb.AppendLine("            Notify (\\_TZ.TZ01, 0x81)");
            sb.AppendLine("            Return (Zero)");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            try
            {
                File.WriteAllText(dslFilePath, sb.ToString());
                res.DslPath = dslFilePath;

                if (File.Exists(iaslPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = iaslPath,
                        Arguments = $"-p \"{amlFilePath}\" \"{dslFilePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();

                        res.LogOutput = stdout + "\n" + stderr;
                        if (proc.ExitCode == 0 && File.Exists(amlFilePath))
                        {
                            res.Success = true;
                            res.AmlPath = amlFilePath;
                        }
                        else
                        {
                            res.ErrorMessage = $"iASL compilation failed with exit code {proc.ExitCode}.";
                        }
                    }
                    else
                    {
                        res.ErrorMessage = "Failed to launch iasl compiler process.";
                    }
                }
                else
                {
                    res.ErrorMessage = $"iASL compiler not found at {iaslPath}.";
                }
            }
            catch (Exception ex)
            {
                res.ErrorMessage = $"Failed generating SSDT override: {ex.Message}";
                AppLogger.Log(res.ErrorMessage);
            }

            return res;
        }
    }
}
