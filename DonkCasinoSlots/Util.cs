
using System;
using UnityEngine;
namespace DonkCasinoSlots    
{
    public static class Util
    {
        public static readonly System.Random Rng = new System.Random();


        static bool HasEnoughDukes(EntityPlayer player, int amount)
        {
            if (player == null || amount <= 0) return false;
            var bag = player.bag as Bag;
            if (bag == null) return false;

            var dukes = ItemClass.GetItem("casinoCoin", false);
            if (dukes.IsEmpty()) return false;

            int have = bag.GetItemCount(dukes);
            return have >= amount;
        }

        public static bool TryTakeDukes(EntityPlayer player, int amount)
        {
            if (player == null || amount <= 0) return false;
            var bag = player.bag as Bag;
            if (bag == null) return false;

            var dukes = ItemClass.GetItem("casinoCoin", false);
            if (dukes.IsEmpty()) return false;

            // Remove up to 'amount' dukes by ItemValue type.
            // Returns the number actually removed.
            int removed = bag.DecItem(dukes, amount, /*_ignoreModdedItems:*/ false, /*_removedItems:*/ null);
            return removed >= amount;
        }

        public static void SendCasinoAction(CasinoActionType type)
        {
            var pkg = NetPackageManager.GetPackage<NetPackageCasinoAction>().Setup(type);
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
