using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace DonkCasinoSlots
{
    public class LootEntry { public string name; public int min; public int max; public int weight; }
    
    public class BucketCfg
    {
        public int Min = 1;        // picks per spin (min)
        public int Max = 1;        // picks per spin (max)
        public int BurstMin = 1;   // how many items to spawn per pick (min)
        public int BurstMax = 1;   // how many items to spawn per pick (max)
        public bool Unique = false; // draw without replacement per spin
    }
    public static class CasinoConfig
    {
        public static int SpinCost = 500;
        public static float PReallyBad = .02f, PBad = .68f, PGood = .28f, PJackpot = .02f;
        public static float PTilted = .35f, PExplode = .1f, PSpawn = .55f;
        public static int OutputSlots = 35;

        public static int RagdollRadius = 10;
        public static float RagdollBaseForce = 1.8f;
        public static float RagdollUpward = .35f;
        public static float RagdollDuration = 1.5f;
        public static string RagdollFalloff = "linear";

        public static BucketCfg ReallyBadRolls = new BucketCfg { Min = 1, Max = 1 };
        public static BucketCfg BadRolls     = new BucketCfg { Min = 0, Max = 2, BurstMin = 1, BurstMax = 2, Unique = false };
        public static BucketCfg GoodRolls    = new BucketCfg { Min = 1, Max = 3, BurstMin = 1, BurstMax = 2, Unique = true  };
        public static BucketCfg JackpotRolls = new BucketCfg { Min = 2, Max = 4, BurstMin = 2, BurstMax = 3, Unique = true  };
        
        public static List<LootEntry> Bad = new List<LootEntry>();
        public static List<LootEntry> Good = new List<LootEntry>();
        public static List<LootEntry> Jackpot = new List<LootEntry>();
        public static void Load()
        {
            try
            {
                var path = GameIO.GetGameDir("Mods/DonkCasinoSlots/Config/casino_loot.xml");
                var doc = new XmlDocument();
                doc.Load(path);

                var ws = doc.SelectSingleNode("//workstation") as System.Xml.XmlElement;
                if (ws != null && int.TryParse(ws.GetAttribute("output_slots"), out var v) && v > 0)
                    OutputSlots = v;
                
                SpinCost = int.Parse(doc.SelectSingleNode("//economy")?.Attributes?["spin_cost_dukes"]?.Value ?? "500");

                var odds = doc.SelectSingleNode("//odds")?.Attributes;
                if (odds != null)
                {
                    PBad     = float.Parse(odds["bad"].Value);
                    PGood    = float.Parse(odds["good"].Value);
                    PJackpot = float.Parse(odds["jackpot"].Value);
                }

                var reallyBadfx = doc.SelectSingleNode("//badEffects")?.Attributes;
                if (reallyBadfx != null)
                {
                    PExplode = float.Parse(reallyBadfx["explode"].Value);
                    PSpawn   = float.Parse(reallyBadfx["spawn"].Value);
                    PTilted = float.Parse(reallyBadfx["tilted"].Value);
                }
                
                Bad     = ReadGroup(doc, "bad");
                Good    = ReadGroup(doc, "good");
                Jackpot = ReadGroup(doc, "jackpot");

                BadRolls     = ReadBucket(doc, "bad",     1, 2);
                GoodRolls    = ReadBucket(doc, "good",    1, 3);
                JackpotRolls = ReadBucket(doc, "jackpot", 2, 5);
                
                NormalizeOdds();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DonkCasinoSlots] Failed to load casino_loot.xml, using defaults. " + e);
            }
        }
        
        static BucketCfg ReadBucket(XmlDocument doc, string name, int dmin, int dmax)
        {
            var n = doc.SelectSingleNode($"//buckets/bucket[@name='{name}']") as XmlElement;
            var cfg = new BucketCfg{ Min=dmin, Max=dmax };
            if (n != null)
            {
                cfg.Min      = Math.Max(0, AttrI(n, "rolls_min", cfg.Min));
                cfg.Max      = Math.Max(cfg.Min, AttrI(n, "rolls_max", cfg.Max));
                cfg.BurstMin = Math.Max(1, AttrI(n, "burst_min", cfg.BurstMin));
                cfg.BurstMax = Math.Max(cfg.BurstMin, AttrI(n, "burst_max", cfg.BurstMax));
                cfg.Unique   = AttrB(n, "unique", cfg.Unique);
            }
            return cfg;
        }

        static List<LootEntry> ReadGroup(XmlDocument doc, string groupName)
        {
            var list = new List<LootEntry>();
            foreach (XmlNode n in doc.SelectNodes($"//group[@name='{groupName}']/item"))
            {
                list.Add(new LootEntry {
                    name = n.Attributes["name"].Value,
                    min  = int.Parse(n.Attributes["min"].Value),
                    max  = int.Parse(n.Attributes["max"].Value),
                    weight = int.Parse(n.Attributes["weight"].Value)
                });
            }
            return list;
        }

        static void NormalizeOdds()
        {
            float s = PReallyBad + PBad + PGood + PJackpot;
            if (s <= 0f)
            { PReallyBad = .02f; PBad=.7f; PGood=.28f; PJackpot=.02f; return; }

            PReallyBad/=s; PBad/=s; PGood/=s; PJackpot/=s;

            s = PTilted + PExplode + PSpawn;
            if (s > 0f)
            { PTilted /=s; PExplode/=s; PSpawn/=s; }
        }
        
        static int AttrI(XmlElement e, string name, int def)
        {
            if (e == null) return def;
            var s = e.GetAttribute(name);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        static float AttrF(XmlElement e, string name, float def)
        {
            if (e == null) return def;
            var s = e.GetAttribute(name);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        static bool AttrB(XmlElement e, string name, bool def)
        {
            if (e == null) return def;
            var s = e.GetAttribute(name);
            if (string.IsNullOrEmpty(s)) return def;
            return s.Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("1") ||
                   s.Equals("yes", System.StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("y", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
