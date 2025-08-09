
using System;
using UnityEngine;
namespace DonkCasinoSlots    
{
    public static class Util
    {
        public static readonly System.Random Rng = new System.Random();

        public static bool TryTakeDukes(EntityPlayer player, int amount)
        {
            var needed = amount;
            var inv = player.bag;
            for (int i = 0; i < inv.GetSlotsCount(); i++)
            {
                var stack = inv.GetSlotItem(i);
                if (stack.IsEmpty()) continue;
                if (stack.itemValue.ItemClass.Name != "casinoCoin") continue;
                int take = Math.Min(needed, stack.count);
                if (take <= 0) break;
                inv.DecItemCount(i, take);
                needed -= take;
                if (needed <= 0) break;
            }
            return needed <= 0;
        }

        public static void SendCasinoAction(XUiC_WorkstationWindowGroup ui, CasinoActionType type)
        {
            // Grab the block position for this workstation
            var te = ui?.tileEntity as TileEntityWorkstation;
            if (te == null) return;
            var pos = te.ToWorldPos();
            var pkg = NetPackageManager.GetPackage<NetPackageCasinoAction>().Setup(type, pos);
            ConnectionManager.Instance.SendToServer(pkg);
        }

        public static Vector3i ToWorldPos(this TileEntity te) => new Vector3i(te.ToWorldPos().x, te.ToWorldPos().y, te.ToWorldPos().z);

        public static int WeightedPick(params (int weight, Action act)[] entries)
        {
            int sum = 0; foreach (var e in entries) sum += e.weight;
            int roll = Rng.Next(0, Math.Max(1, sum));
            int acc = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                acc += entries[i].weight;
                if (roll < acc) return i;
            }
            return entries.Length - 1;
        }
    }
}
