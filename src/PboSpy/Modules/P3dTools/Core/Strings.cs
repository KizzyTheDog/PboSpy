using PboSpy.Localization;

namespace PboSpy.Modules.P3dTools.Core;

/// <summary>P3D Tools strings live in PboSpy's language files under the "P3D." prefix.</summary>
internal static class Strings
{
    public static string T(string key, params object[] args)
        => args.Length == 0 ? Loc.T("P3D." + key) : Loc.F("P3D." + key, args);
}
