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
            CasinoConfig.Load(); // read Config/casino_loot.xml
            Log.Out("[DonkCasinoSlots] Loaded. Spin cost: {0}", CasinoConfig.SpinCost);
        }
    }

    // Hook the workstation window buttons
    [HarmonyPatch(typeof(XUiC_WorkstationWindowGroup), "OnOpen")]
    public class Patch_WorkstationOpen
    {
        static void Postfix(XUiC_WorkstationWindowGroup __instance)
        {
            if (__instance.WindowGroup == null) return;
            if (__instance.WindowGroup.Name != "workstation_casino_slot") return;
            var te = __instance?.tileEntity as TileEntityWorkstation;
            if (te != null && __instance.WindowGroup?.Name == "workstation_casino_slot")
                CasinoSlotLogic.EnsureOutputSize(te, CasinoConfig.OutputSlots);

            
            var spinBtn   = __instance.WindowGroup.Controller.GetChildById("btnCasinoSpin")   as XUiController;
            var doubleBtn = __instance.WindowGroup.Controller.GetChildById("btnCasinoDouble") as XUiController;

            if (spinBtn != null)   spinBtn.OnPress += (_, __) => Util.SendCasinoAction(__instance, CasinoActionType.Spin);
            if (doubleBtn != null) doubleBtn.OnPress += (_, __) => Util.SendCasinoAction(__instance, CasinoActionType.DoubleOrNothing);
        }
    }

    // Register our NetPackage on both sides
    [HarmonyPatch(typeof(NetPackageManager), "RegisterNetPackages")]
    public class Patch_RegisterPkg
    {
        static void Postfix(NetPackageManager __instance)
        {
            __instance.RegisterNetPackage<NetPackageCasinoAction>();
        }
    }
}