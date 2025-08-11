using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace DonkCasinoSlots
{
    public class ModEntry : IModApi
    {
        public static Harmony Harmony;

        public void InitMod(Mod mod)
        {
            Harmony = new Harmony("com.donk.casino.v21");
            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            var t = typeof(NetPackageCasinoAction);
            if (!NetPackageManager.knownPackageTypes.ContainsKey(t.Name))
                NetPackageManager.knownPackageTypes[t.Name] = t;
            CasinoConfig.Load(); // read Config/casino_loot.xml
            Debug.Log("[DonkCasinoSlots] Harmony Loaded");
        }
    }

    [HarmonyPatch(typeof(XUiC_WorkstationWindowGroup), "OnOpen")]
    static class Patch_WorkstationOpen
    {
        static void Postfix(XUiC_WorkstationWindowGroup __instance)
        {
            // If our buttons exist in this window, wire them. If not, do nothing.
            TryWire(__instance, "btnCasinoSpin",   CasinoActionType.Spin);
            TryWire(__instance, "btnCasinoDouble", CasinoActionType.DoubleOrNothing);
        }

        static void TryWire(XUiController root, string id, CasinoActionType action)
        {
            var btn = root?.GetChildById(id) as XUiController;
            if (btn == null) return;
            btn.OnPress += (_, __) => Util.SendCasinoAction(action);
        }
    }
}