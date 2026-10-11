using System;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(DuplicateItemFix.Mod), "DuplicateItemFix", "1.0.0", "Lycanthor")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace DuplicateItemFix;

public sealed class Mod : MelonMod
{

    internal static Mod Instance { get; private set; } = null!;
    internal static string RunMarker { get; private set; } = string.Empty;
    internal static MelonLogger.Instance Log => Instance.LoggerInstance;

    public override void OnInitializeMelon()
    {
        Instance = this;
        RunMarker = $"DIF-{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";

        Assembly assembly = typeof(Mod).Assembly;

        string informational =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

        LoggerInstance.Msg(
            $"version={informational}; marker={RunMarker}; {Repair.Describe()}; " +
            $"{GearRepair.Describe()}; " +
            (Repair.Enabled
                ? "repair runs once per region load."
                : "repair is off by preference; stored spawner data will not be changed."));
    }

}
