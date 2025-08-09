using Platform;
using UnityEngine;

namespace DonkCasinoSlots
{
    public enum CasinoActionType { Spin, DoubleOrNothing }

    public class NetPackageCasinoAction : NetPackage
    {
        private CasinoActionType action;
        private Vector3i blockPos;

        public NetPackageCasinoAction Setup(CasinoActionType action, Vector3i blockPos)
        {
            this.action = action;
            this.blockPos = blockPos;
            return this;
        }

        public override void read(PooledBinaryReader br)
        {
            action = (CasinoActionType)br.ReadByte();
            blockPos = new Vector3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
        }

        public override void write(PooledBinaryWriter bw)
        {
            bw.Write((byte)action);
            bw.Write(blockPos.x); bw.Write(blockPos.y); bw.Write(blockPos.z);
        }

        public override void ProcessPackage(World world, GameManagerCallbacks callbacks)
        {
            // Server authoritative
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;

            var player = world.GetPrimaryPlayer(); // fallback; prefer by sender ID:
            if (Sender != null && Sender.entityId != -1)
                player = world.GetEntity(Sender.entityId) as EntityPlayer;

            if (player == null) return;

            CasinoLogic.HandleAction(world, player, blockPos, action);
        }

        public override int GetLength() => 1 + 4 * 3;
    }
}
