// Standalone probe. Not part of the PreySense app.
// Built against DtsCntInterop.dll, imported from the installed
// C:\Windows\System32\DTS\PC\APO4x\dtscnt64.dll type library.
//
//   tlbimp dtscnt64.dll /namespace:DtsCnt /out:DtsCntInterop.dll
//   csc /nologo /r:DtsCntInterop.dll Program.cs
//
// --test switches Speakers to Movie, checks the registry, then restores Music.

using System;
using System.Text;
using DtsCnt;
using Microsoft.Win32;

internal static class Program
{
    private const string ApoDirectory = @"C:\Windows\System32\DTS\PC\APO4x";

    private static int Main(string[] args)
    {
        bool test = args.Length > 0 && args[0] == "--test";
        DTSAPOSystemClass system = new DTSAPOSystemClass();
        system.InitializeSystem(ApoDirectory);

        IDTSEndpointEnumerator endpoints = (IDTSEndpointEnumerator)system.EnumerateDTSEndpoints();
        IDTSEndpointInfo speakers = null;
        string originalSpeakerMode = null;

        while (true)
        {
            IDTSEndpointInfo endpoint = NextEndpoint(endpoints);
            if (endpoint == null)
                break;

            IDTSOperatingMode current = (IDTSOperatingMode)endpoint.OpMode;
            Console.WriteLine(endpoint.FriendlyName);
            Console.WriteLine("  registry  " + endpoint.RegistryPath);
            Console.WriteLine("  config    " + endpoint.ConfigFile);
            Console.WriteLine("  active    " + current.Name + "  APOEnable=" + current.APOEnable);
            Console.WriteLine("  saved     " + ReadCurrentMode(endpoint.RegistryPath));

            IDTSAPOOpModeEnumerator modes = (IDTSAPOOpModeEnumerator)endpoint.GetOpModeEnumerator();
            while (true)
            {
                IDTSOperatingMode mode = NextMode(modes);
                if (mode == null)
                    break;
                Console.WriteLine("  mode      " + mode.Name);
            }

            if (endpoint.FriendlyName.IndexOf("Speakers", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                speakers = endpoint;
                originalSpeakerMode = current.Name;
            }
        }

        if (!test)
            return 0;

        if (speakers == null || originalSpeakerMode == null)
        {
            Console.Error.WriteLine("No Speakers endpoint.");
            return 1;
        }

        IDTSOperatingMode movie = FindMode(speakers, "APO4-Movie/APO4-Internal Stereo");
        IDTSOperatingMode restore = FindMode(speakers, originalSpeakerMode);
        if (movie == null || restore == null)
        {
            Console.Error.WriteLine("Movie or original mode was not in the enumerator.");
            return 1;
        }

        try
        {
            speakers.OpMode = (DTSOperatingMode)movie;
            string after = ReadCurrentMode(speakers.RegistryPath);
            string live = ((IDTSOperatingMode)speakers.OpMode).Name;
            Console.WriteLine("switched   " + live);
            Console.WriteLine("registry   " + after);
            bool ok = live.IndexOf("APO4-Movie", StringComparison.Ordinal) >= 0
                && after.IndexOf("APO4-Movie", StringComparison.Ordinal) >= 0;
            Console.WriteLine(ok ? "RESULT     registry followed the COM set" : "RESULT     COM set did not stick");
            return ok ? 0 : 2;
        }
        finally
        {
            speakers.OpMode = (DTSOperatingMode)restore;
            Console.WriteLine("restored   " + ((IDTSOperatingMode)speakers.OpMode).Name);
            Console.WriteLine("registry   " + ReadCurrentMode(speakers.RegistryPath));
        }
    }

    private static IDTSOperatingMode FindMode(IDTSEndpointInfo endpoint, string name)
    {
        IDTSAPOOpModeEnumerator modes = (IDTSAPOOpModeEnumerator)endpoint.GetOpModeEnumerator();
        while (true)
        {
            IDTSOperatingMode mode = NextMode(modes);
            if (mode == null)
                return null;
            if (mode.Name.IndexOf(name, StringComparison.Ordinal) >= 0)
                return mode;
        }
    }

    private static IDTSEndpointInfo NextEndpoint(IDTSEndpointEnumerator endpoints)
    {
        try
        {
            object next = endpoints.Next();
            return next == null ? null : (IDTSEndpointInfo)next;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static IDTSOperatingMode NextMode(IDTSAPOOpModeEnumerator modes)
    {
        try
        {
            object next = modes.Next();
            return next == null ? null : (IDTSOperatingMode)next;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static string ReadCurrentMode(string registryPath)
    {
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
        {
            if (key == null)
                return "<missing>";
            byte[] blob = key.GetValue("DtsApoCurrOpMode") as byte[];
            if (blob == null)
                return "<no DtsApoCurrOpMode>";
            string ascii = Encoding.ASCII.GetString(blob);
            StringBuilder text = new StringBuilder();
            foreach (char c in ascii)
            {
                if (c >= 32 && c < 127)
                    text.Append(c);
                else if (text.Length > 0 && text[text.Length - 1] != ' ')
                    text.Append(' ');
            }
            return text.ToString().Trim();
        }
    }
}
