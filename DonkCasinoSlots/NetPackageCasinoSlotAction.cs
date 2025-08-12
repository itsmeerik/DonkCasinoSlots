using System;

namespace DonkCasinoSlots
{

    
    public enum CasinoActionType : byte
    {
        Spin = 0,
        DoubleOrNothing = 1
    }

    public class NetPackageCasinoSlotAction : NetPackage
    {
        private CasinoActionType action;

        public NetPackageCasinoSlotAction Setup(CasinoActionType a)
        {
            action = a;
            return this;
        }

        public override void write(PooledBinaryWriter bw)
        {
            ((System.IO.BinaryWriter)bw).Write((byte)action); // force base-class overload
        }

        public override void read(PooledBinaryReader br)
        {
            action = (CasinoActionType)br.ReadByte(); // deserialize to enum
        }

        public override void ProcessPackage(World world, GameManager gm)
        {
            if (!ConnectionManager.Instance.IsServer) return;

            var eid = this.Sender?.entityId ?? -1;
            var player = eid != -1 ? world.GetEntity(eid) as EntityPlayer : world.GetPrimaryPlayer();
            if (player == null) return;

            var te = FindNearbyCasinoSlot(world, player, 5);
            if (te == null) return;

            CasinoSlotLogic.EnsureOutputSize(te, CasinoConfig.OutputSlots);
            CasinoSlotLogic.HandleAction(world, player, te.ToWorldPos(), action); // <— enum
        }

        public override int GetLength() => 1;

        // Finds the nearest workstation TE that corresponds to *your* casino slot block
        static TileEntityWorkstation FindNearbyCasinoSlot(World world, EntityPlayer player, int radius)
        {
            var p = player.GetBlockPosition();

            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -2; dy <= 2; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var pos = new Vector3i(p.x + dx, p.y + dy, p.z + dz);

                // Must be a workstation TE
                var te = world.GetTileEntity(pos) as TileEntityWorkstation;
                if (te == null) continue;

                var bv = world.GetBlock(pos);
                int idx = (int)bv.type;
                if (idx < 0 || idx >= Block.list.Length) continue;
                var block = Block.list[idx];
                if (block == null) continue;

                // Option A: match by your block's name from blocks.xml
                // (e.g., <block name="cntCasinoSlot" .../>)
                var blockName = block.GetBlockName(); // v2.1 method name
                if (!string.IsNullOrEmpty(blockName) &&
                    blockName.Equals("Casino Slot Machine", StringComparison.OrdinalIgnoreCase))
                    return te;

                // Option B: match by the Workstation property defined in your blocks.xml
                // (e.g., <property name="Workstation" value="casino_slot"/>)
                var props = block.Properties?.Values;
                string ws = props != null ? props["Workstation"] : null;
                if (!string.IsNullOrEmpty(ws) &&
                    ws.Equals("casino_slot", StringComparison.OrdinalIgnoreCase))
                    return te;
            }

            return null;
        }

    }
}
