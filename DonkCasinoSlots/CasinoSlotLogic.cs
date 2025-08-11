using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
namespace DonkCasinoSlots
{
    public static class CasinoSlotLogic
    {
        public static void EnsureOutputSize(TileEntityWorkstation te, int desired)
        {
            try
            {
                if (te.output != null && te.output.Length >= desired) return;

                var old = te.output ?? Array.Empty<ItemStack>();
                var newer = new ItemStack[desired];
                var n = Math.Min(old.Length, newer.Length);
                for (int i = 0; i < n; i++) newer[i] = old[i];

                // assign (direct or via reflection fallback)
                try {
                    te.output = newer;
                } catch {
                    var f = typeof(TileEntityWorkstation).GetField("output",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    f?.SetValue(te, newer);
                }

                te.SetModified();
            }
            catch { /* best-effort; don’t crash the server */ }
        }

        public static void HandleAction(World world, EntityPlayer player, Vector3i blockPos, CasinoActionType action)
        {
            var te = world.GetTileEntity(blockPos) as TileEntityWorkstation;
            if (te == null) return;

            EnsureOutputSize(te, CasinoConfig.OutputSlots);
            
            // Use the workstation's OUTPUT inventory as the winnings pool
            var output = te.output;

            switch (action)
            {
                case CasinoActionType.Spin:
                    DoSpin(world, player, te, output);
                    break;
                case CasinoActionType.DoubleOrNothing:
                    DoDoubleOrNothing(world, player, te, output);
                    break;
            }

            te.SetModified(); // sync to clients
        }
        
        static void DoSpin(World world, EntityPlayer player, TileEntityWorkstation te, ItemStack[] output)
        {
            if (!Util.TryTakeDukes(player, CasinoConfig.SpinCost))
            {
                SdtdConsole.Instance.Output($"[Casino] Not enough dukes ({CasinoConfig.SpinCost}).");
                return;
            }

            // Choose bucket
            float r = (float)Util.Rng.NextDouble();
            string bucket;
            if (r < CasinoConfig.PBad) bucket = "bad";
            else if (r < CasinoConfig.PBad + CasinoConfig.PReallyBad) bucket = "reallybad";
            else if (r < CasinoConfig.PBad + CasinoConfig.PReallyBad + CasinoConfig.PGood) bucket = "good";
            else bucket = "jackpot";

            bool addedAny = false;

            switch (bucket)
            {
                case "bad":
                    // Roll from the "bad" list using configured picks and bursts.
                    // If you want "pure nothing most of the time", set rolls_min/max low and keep a heavy-weight "nothing" entry in the Bad group.
                    addedAny = RollGroupIntoOutput(world, te, output, CasinoConfig.Bad, CasinoConfig.BadRolls, player.entityId);
                    if (!addedAny)
                        world.GetGameManager().PlaySoundAtPositionServer(te.ToWorldPos().ToVector3(), "ui_denied", AudioRolloffMode.Linear, 30);
                    break;

                case "reallybad":
                {
                    float rb = (float)Util.Rng.NextDouble();
                    if (rb < CasinoConfig.PTilted) ApplyTilted(player);
                    else if (rb < CasinoConfig.PTilted + CasinoConfig.PExplode) BlowUpMachine(world, te);
                    else SpawnZombies(world, player, count: 2);
                    break;
                }

                case "good":
                    addedAny = RollGroupIntoOutput(world, te, output, CasinoConfig.Good, CasinoConfig.GoodRolls, player.entityId);
                    if (addedAny)
                        world.GetGameManager()
                            .PlaySoundAtPositionServer(te.ToWorldPos().ToVector3(), "ui_trader_purchase", AudioRolloffMode.Linear, 30);
                    break;

                case "jackpot":
                    addedAny = RollGroupIntoOutput(world, te, output, CasinoConfig.Jackpot, CasinoConfig.JackpotRolls, player.entityId);
                    if (addedAny)
                        GameManager.Instance.ChatMessageServer(
                            null, EChatType.Global, -1,
                            $"{player.EntityName} hit the JACKPOT!",
                            null, EMessageSender.Server);       
                    break;
            }
        }

        static void DoDoubleOrNothing(World world, EntityPlayer player, TileEntityWorkstation te, ItemStack[] output)
        {
            bool hasAny = false;
            foreach (var s in output)
                if (!s.IsEmpty())
                {
                    hasAny = true;
                    break;
                }

            if (!hasAny) return;

            bool win = Util.Rng.NextDouble() < 0.5; // tweakable if you want
            if (!win)
            {
                Array.Clear(output, 0, output.Length);
                world.GetGameManager().PlaySoundAtPositionServer(te.ToWorldPos().ToVector3(), "ui_denied", AudioRolloffMode.Linear, 30);
                return;
            }

            // Double each stack (respect stack limits; overflow tries to spread, then drop to world)
            var toAdd = new List<ItemStack>();
            for (int i = 0; i < output.Length; i++)
            {
                var stack = output[i];
                if (stack.IsEmpty()) continue;
                int target = stack.count * 2;
                int clamp = Math.Min(target, stack.itemValue.ItemClass.Stacknumber.Value);
                int extra = target - clamp;
                stack.count = clamp;
                output[i] = stack;
                if (extra > 0) toAdd.Add(new ItemStack(stack.itemValue, extra));
            }

            foreach (var add in toAdd)
            {
                if (!TryMergeInto(output, add))
                    GameManager.Instance.ItemDropServer(add, te.ToWorldPos().ToVector3() + Vector3.up * 1.2f, Vector3.zero, -1, 60f, false);
            }

            world.GetGameManager().PlaySoundAtPositionServer(te.ToWorldPos().ToVector3(), "ui_mission_complete", AudioRolloffMode.Linear, 30);
        }

        // === Loot rolling with picks + bursts (and optional unique draws) ===
        static bool RollGroupIntoOutput(
            World world,
            TileEntityWorkstation te,
            ItemStack[] output,
            List<LootEntry> group,
            BucketCfg cfg,
            int ownerEntityId)
        {
            if (group == null || group.Count == 0) return false;

            // Determine number of picks for this spin from this bucket
            int picks = UnityEngine.Random.Range(Math.Max(0, cfg.Min), Math.Max(cfg.Min, cfg.Max) + 1);
            if (picks <= 0) return false;

            // Optional unique draws per spin
            List<LootEntry> pool = cfg.Unique ? new List<LootEntry>(group) : group;

            bool any = false;

            for (int i = 0; i < picks; i++)
            {
                // How many items to produce for THIS pick
                int burst = UnityEngine.Random.Range(Math.Max(1, cfg.BurstMin),
                    Math.Max(cfg.BurstMin, cfg.BurstMax) + 1);

                for (int b = 0; b < burst; b++)
                {
                    var e = Pick(pool);
                    if (e == null || e.name == "nothing") continue;

                    int count = UnityEngine.Random.Range(e.min, e.max + 1);
                    if (count <= 0) continue;

                    GiveToOutput(world, te, output, e.name, count, ownerEntityId);
                    any = true;

                    if (cfg.Unique)
                    {
                        // Remove this entry from the pool so we don't pick it again this spin
                        // (Stops when pool empties)
                        for (int idx = 0; idx < pool.Count; idx++)
                        {
                            if (pool[idx].name == e.name)
                            {
                                pool.RemoveAt(idx);
                                break;
                            }
                        }

                        if (pool.Count == 0) break; // out of unique options
                    }
                }

                if (cfg.Unique && pool.Count == 0) break;
            }

            return any;
        }

        static LootEntry Pick(List<LootEntry> list)
        {
            if (list == null || list.Count == 0) return null;
            int sum = 0;
            foreach (var e in list) sum += e.weight;
            int roll = Util.Rng.Next(0, Math.Max(1, sum));
            int acc = 0;
            foreach (var e in list)
            {
                acc += e.weight;
                if (roll < acc) return e;
            }

            return list[list.Count - 1];
        }


        static void GiveToOutput(World world, TileEntityWorkstation te, ItemStack[] output, string name, int count, int ownerEntityId)
        {
            var iv = ItemClass.GetItem(name, false);
            if (iv.IsEmpty()) return;

            var stack = new ItemStack(iv, count);

            if (!TryMergeInto(output, stack))
            {
                // Try empty slots
                for (int i = 0; i < output.Length && stack.count > 0; i++)
                {
                    if (!output[i].IsEmpty()) continue;
                    int max = iv.ItemClass.Stacknumber.Value;
                    int move = Math.Min(max, stack.count);
                    output[i] = new ItemStack(iv, move);
                    stack.count -= move;
                }
            }

            // Spill to world if still leftover
            if (stack.count > 0)
            {
                var dropPos = te.ToWorldPos().ToVector3() + Vector3.up * 1.2f;

                // If you have the player/owner id in scope, pass it; otherwise use -1

                try
                {
                    // Common v2.x signature
                    world.gameManager.ItemDropServer(
                        new ItemStack(iv, stack.count),
                        dropPos,
                        Vector3.zero,
                        ownerEntityId,
                        60f);
                }
                catch
                {
                    // Fallback (older signature without owner/lifetime)
                    world.gameManager.ItemDropServer(
                        new ItemStack(iv, stack.count),
                        dropPos,
                        Vector3.zero);
                }
            }
        }

        static bool TryMergeInto(ItemStack[] grid, ItemStack add)
        {
            // merge with same item
            for (int i = 0; i < grid.Length && add.count > 0; i++)
            {
                var s = grid[i];
                if (s.IsEmpty()) continue;
                if (!s.itemValue.Equals(add.itemValue)) continue;
                int max = s.itemValue.ItemClass.Stacknumber.Value;
                int space = max - s.count;
                if (space <= 0) continue;
                int move = Math.Min(space, add.count);
                s.count += move;
                grid[i] = s;
                add.count -= move;
            }

            // fill empties
            for (int i = 0; i < grid.Length && add.count > 0; i++)
            {
                if (!grid[i].IsEmpty()) continue;
                int max = add.itemValue.ItemClass.Stacknumber.Value;
                int move = Math.Min(max, add.count);
                grid[i] = new ItemStack(add.itemValue, move);
                add.count -= move;
            }

            return add.count <= 0;
        }

        // === Really Bad effects ===
        static void ApplyTilted(EntityPlayer player)
        {
            player.Buffs?.AddBuff("buffCasinoTilted");
            GameManager.Instance.ChatMessageServer(
                null, EChatType.Global, -1,
                $"{player.EntityName} is TILTED!", 
                null, EMessageSender.Server);
        }

        static void BlowUpMachine(World world, TileEntity te)
        {
            var pos = te.ToWorldPos();
            world.GetGameManager().PlaySoundAtPositionServer(pos.ToVector3(), "explosion_small", AudioRolloffMode.Linear, 30);
            world.SetBlockRPC(pos, BlockValue.Air); // destroy the block cleanly

            RagdollBurst(world, pos.ToVector3() + Vector3.up * 0.5f,
                CasinoConfig.RagdollRadius,
                CasinoConfig.RagdollBaseForce,
                CasinoConfig.RagdollUpward,
                CasinoConfig.RagdollDuration,
                CasinoConfig.RagdollFalloff);
        }

        static void SpawnZombies(World world, EntityPlayer player, int count)
        {
            string[] pool = { "zombieArlene", "zombieBoe", "zombieYo" };
            for (int i = 0; i < count; i++)
            {
                var name = pool[UnityEngine.Random.Range(0, pool.Length)];
                int cls = EntityClass.FromString(name);
                if (cls < 0) continue;
                var pos = player.position +
                          new Vector3(UnityEngine.Random.Range(-2, 3), 0, UnityEngine.Random.Range(-2, 3));
                var ent = EntityFactory.CreateEntity(cls, pos);
                world.SpawnEntityInWorld(ent);
            }
        }

        static void RagdollBurst(World world, Vector3 origin, float radius, float baseForce, float upwardBias,
            float duration, string falloff)
        {
            // Grab all players we can see (server-side)
            var players = GameManager.Instance.World?.Players?.list;
            if (players == null) return;

            foreach (var ep in players)
            {
                if (ep == null || ep.IsDead()) continue;

                var toPlayer = ep.position - origin;
                float dist = toPlayer.magnitude;
                if (dist > radius) continue;

                // Scale force by distance if using linear falloff
                float scale = 1f;
                if (string.Equals(falloff, "linear", System.StringComparison.OrdinalIgnoreCase))
                    scale = Mathf.Clamp01(1f - (dist / Mathf.Max(0.001f, radius)));

                var dir = (dist > 0.01f ? toPlayer.normalized : Vector3.forward);
                var push = (dir + Vector3.up * Mathf.Clamp01(upwardBias)).normalized * (baseForce * scale);

                TryRagdollAndPush(ep, push, duration);
            }
        }

// Try multiple strategies to ragdoll + impulse the player so this keeps working across minor builds:
        static void TryRagdollAndPush(EntityPlayer ep, Vector3 push, float durationSec)
        {
            bool ragdolled = false;

            // 1) Preferred: call an internal ragdoll method if it exists (names differ across builds).
            try
            {
                // Common possibilities across versions:
                // StartRagdoll(float seconds), StartRagdoll(Vector3 force, float seconds),
                // SetRagdoll(bool), or SetRagdollState(bool)
                var t = typeof(EntityAlive);

                MethodInfo m =
                    t.GetMethod("StartRagdoll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(float) }, null)
                    ?? t.GetMethod("StartRagdoll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(Vector3), typeof(float) }, null)
                    ?? t.GetMethod("SetRagdoll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(bool) }, null)
                    ?? t.GetMethod("SetRagdollState",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                        new[] { typeof(bool) }, null);

                if (m != null)
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(Vector3))
                        m.Invoke(ep, new object[] { push, durationSec });
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(float))
                        m.Invoke(ep, new object[] { durationSec });
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(bool))
                        m.Invoke(ep, new object[] { true });
                    ragdolled = true;
                }
            }
            catch
            {
                /* best-effort */
            }

            // 2) Apply a “stun/knockdown” style buff if available (encourages ragdoll in some builds)
            try
            {
                if (!ragdolled)
                    ep.Buffs?.AddBuff("buffStunned"); // if your build has a different name, update it
            }
            catch
            {
                /* optional */
            }

            // 3) Impulse / knockback: use DamageEntity with explosion-type source (safe in most builds)
            try
            {
                var ds = new DamageSource(EnumDamageSource.Internal, EnumDamageTypes.Bashing);
                // Strength=1 (tiny) + large impulse scales better than large damage.
                // Last param is often "impulseScale" in recent builds.
                ep.DamageEntity(ds, 1, false, Mathf.Max(1f, push.magnitude * 2f));
            }
            catch
            {
                /* some builds have different signature; we add another try below */
            }

            // 4) Last resort push via motion (small nudge even if ragdoll didn’t trigger)
            try
            {
                // Give them a little pop in the intended direction
                ep.motion += new Vector3(push.x, Mathf.Max(0.1f, push.y), push.z);
            }
            catch
            {
                /* okay to ignore */
            }
        }
    }
}
