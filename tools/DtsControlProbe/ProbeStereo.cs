using System;
using DtsCnt;

internal static class ProbeStereo
{
    private static int Main()
    {
        DTSAPOSystemClass system = new DTSAPOSystemClass();
        system.InitializeSystem(@"C:\Windows\System32\DTS\PC\APO4x");
        IDTSEndpointEnumerator endpoints = (IDTSEndpointEnumerator)system.EnumerateDTSEndpoints();
        object raw = endpoints.Next();
        IDTSEndpointInfo endpoint = (IDTSEndpointInfo)raw;
        string activeBefore = ((IDTSOperatingMode)endpoint.OpMode).Name;
        Console.WriteLine("active before " + activeBefore);
        IDTSOperatingMode custom = Find(endpoint, "APO4-Custom Audio/APO4-Internal Stereo");
        IDTSAPOControl2 stereo = (IDTSAPOControl2)(object)((IDTSAPOControlEnumerator2)custom.get_DTSAPOControlEnumerator(DTSAPOControlType.kDTSAPO_Cont_Public)).GetControlFromName("Eagle-Stereo Mode");
        int original = stereo.Value;
        string originalStr = stereo.StrValue;
        Console.WriteLine("stereo original " + original + " " + originalStr);
        try
        {
            for (int i = stereo.MinValue; i <= stereo.MaxValue; i++)
            {
                stereo.Value = i;
                Console.WriteLine("  " + i + " = " + stereo.StrValue);
            }
        }
        finally
        {
            stereo.Value = original;
            Console.WriteLine("stereo restored " + stereo.Value + " " + stereo.StrValue);
            Console.WriteLine("active after " + ((IDTSOperatingMode)endpoint.OpMode).Name);
        }

        IDTSAPOControl2 lc = (IDTSAPOControl2)(object)((IDTSAPOControlEnumerator2)custom.get_DTSAPOControlEnumerator(DTSAPOControlType.kDTSAPO_Cont_Public)).GetControlFromName("SFX:Eagle-LC Enable");
        Console.WriteLine("LC enable " + lc.Value + " str=" + lc.StrValue + " min=" + lc.MinValue + " max=" + lc.MaxValue);
        IDTSAPOControl2 bass = (IDTSAPOControl2)(object)((IDTSAPOControlEnumerator2)custom.get_DTSAPOControlEnumerator(DTSAPOControlType.kDTSAPO_Cont_Public)).GetControlFromName("MFX:Eagle-TBHDX Bass Level");
        Console.WriteLine("bass level " + bass.Value + " min=" + bass.MinValue + " max=" + bass.MaxValue + " scale=" + bass.Scale);
        return 0;
    }

    private static IDTSOperatingMode Find(IDTSEndpointInfo endpoint, string fragment)
    {
        IDTSAPOOpModeEnumerator modes = (IDTSAPOOpModeEnumerator)endpoint.GetOpModeEnumerator();
        while (true)
        {
            object raw = modes.Next();
            if (raw == null)
                return null;
            IDTSOperatingMode mode = (IDTSOperatingMode)raw;
            if (mode.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                return mode;
        }
    }
}
