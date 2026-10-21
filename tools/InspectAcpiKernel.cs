using System;
using System.Text;
using Microsoft.Win32;

class Program
{
    static void Main()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI\SSDJ\ACRSYS\ACRPRDCT\00001000");
        if (key == null)
        {
            Console.WriteLine("Key not found");
            return;
        }

        byte[] bytes = (byte[])key.GetValue("00000000");
        Console.WriteLine($"Length: {bytes.Length}");

        string raw = Encoding.ASCII.GetString(bytes);
        int wmbh = raw.IndexOf("WMBH");
        int xmbh = raw.IndexOf("XMBH");

        Console.WriteLine($"WMBH index: {wmbh}");
        Console.WriteLine($"XMBH index: {xmbh}");

        using var dsdtKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI\DSDT\ACRSYS\ACRPRDCT\00000002");
        if (dsdtKey != null)
        {
            byte[] dsdtBytes = (byte[])dsdtKey.GetValue("00000000");
            string dsdtRaw = Encoding.ASCII.GetString(dsdtBytes);
            int q30 = dsdtRaw.IndexOf("_Q30");
            int xq30 = dsdtRaw.IndexOf("XQ30");
            Console.WriteLine($"DSDT _Q30 index: {q30}");
            Console.WriteLine($"DSDT XQ30 index: {xq30}");
        }
    }
}
