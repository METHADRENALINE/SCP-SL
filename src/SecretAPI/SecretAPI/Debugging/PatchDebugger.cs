#if DEBUG

namespace SecretAPI.Debugging;

using SecretAPI.Attributes;

internal static class PatchDebugger
{
    [CallOnLoad]
    internal static void Load()
    {
        SecretApi.Harmony.PatchAll(SecretApi.Assembly);
    }
}

#endif
