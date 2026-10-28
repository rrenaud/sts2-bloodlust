using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Bloodlust;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        new Harmony("rrenaud.Bloodlust").PatchAll(Assembly.GetExecutingAssembly());
        if (!MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn)
            FightCapture.Initialize();
    }
}
