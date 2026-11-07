namespace SecretAPI;

using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LabApi.Features;
using LabApi.Loader.Features.Paths;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;

public class SecretApi : Plugin
{
    public override string Name => "SecretAPI";

    public override string Description => "API for SCP:SL";

    public override string Author => "@obvEve";

    public override LoadPriority Priority => LoadPriority.Highest;

    public override Version Version { get; } = Assembly.GetName().Version;

    public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;

    public override bool IsTransparent => true;

    internal static Harmony Harmony { get; } = new("SecretAPI" + DateTime.Now);

    internal static Assembly Assembly { get; } = typeof(SecretApi).Assembly;

    internal static DirectoryInfo ConfigDirectory => field ??= PathManager.Configs.CreateSubdirectory("SecretAPI");

    public override void Enable()
    {
        SecretApiGenerated.OnLoad();
    }

    public override void Disable()
    {
        Harmony.UnpatchAll(Harmony.Id);
    }
}
