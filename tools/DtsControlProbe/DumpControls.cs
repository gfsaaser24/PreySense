using System;
using DtsCnt;

internal static class DumpControls
{
    private static int Main()
    {
        DTSAPOSystemClass system = new DTSAPOSystemClass();
        system.InitializeSystem(@"C:\Windows\System32\DTS\PC\APO4x");
        IDTSEndpointEnumerator endpoints = (IDTSEndpointEnumerator)system.EnumerateDTSEndpoints();
        while (true)
        {
            object raw = endpoints.Next();
            if (raw == null)
                break;
            IDTSEndpointInfo endpoint = (IDTSEndpointInfo)raw;
            IDTSOperatingMode active = (IDTSOperatingMode)endpoint.OpMode;
            Console.WriteLine("ENDPOINT " + endpoint.FriendlyName);
            Console.WriteLine("  active " + active.Name);
            IDTSAPOOpModeEnumerator modes = (IDTSAPOOpModeEnumerator)endpoint.GetOpModeEnumerator();
            while (true)
            {
                object modeRaw = modes.Next();
                if (modeRaw == null)
                    break;
                IDTSOperatingMode mode = (IDTSOperatingMode)modeRaw;
                if (mode.Name.IndexOf("APO4-Custom Audio/APO4-Internal Stereo", StringComparison.Ordinal) < 0
                    && mode.Name.IndexOf("APO4-Music/APO4-Internal Stereo", StringComparison.Ordinal) < 0)
                    continue;
                Console.WriteLine("MODE " + mode.Name);
                Dump(mode, DTSAPOControlType.kDTSAPO_Cont_Public);
            }
            break;
        }
        return 0;
    }

    private static void Dump(IDTSOperatingMode mode, DTSAPOControlType kind)
    {
        Console.WriteLine("  controls " + kind);
        IDTSAPOControlEnumerator enumerator = mode.get_DTSAPOControlEnumerator(kind);
        IDTSAPOControlEnumerator2 named = (IDTSAPOControlEnumerator2)enumerator;
        int n = 0;
        while (n < 80)
        {
            object raw;
            try { raw = enumerator.Next(); }
            catch (Exception ex) { Console.WriteLine("  end " + ex.Message); break; }
            if (raw == null)
                break;
            n++;
            IDTSAPOControl control = (IDTSAPOControl)raw;
            IDTSAPOControl2 text = (IDTSAPOControl2)raw;
            string str = "";
            try { str = text.StrValue ?? ""; } catch { str = "<str fail>"; }
            Console.WriteLine("  [" + n + "] display=" + control.DisplayValue
                + " value=" + control.Value
                + " min=" + control.MinValue
                + " max=" + control.MaxValue
                + " scale=" + control.Scale
                + " str=" + str);
        }
        string[] names =
        {
            "Eagle-Stereo Mode",
            "SFX:Eagle-I3DA Room Index",
            "SFX:Eagle-I3DA Enable",
            "MFX:Eagle-TBHDX Enable",
            "MFX:Eagle-Treble Level",
            "SFX:Eagle-DE Enable",
            "SFX:Eagle-DE Level",
            "MFX:Eagle-Loudness Enable",
            "Eagle-Loudness Enable"
        };
        foreach (string name in names)
        {
            try
            {
                DTSAPOCOMControl found = named.GetControlFromName(name);
                IDTSAPOControl control = (IDTSAPOControl)(object)found;
                Console.WriteLine("  NAME " + name + " => " + control.DisplayValue + " = " + control.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  NAME " + name + " missing: " + ex.GetType().Name);
            }
        }
    }
}
