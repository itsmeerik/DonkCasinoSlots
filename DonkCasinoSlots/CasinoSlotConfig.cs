using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace DonkCasinoSlots
{
    public class LootEntry { public string name; public int min; public int max; public int weight; }

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
        
        public class BucketCfg { public int Min=1, Max=1; };

        public static BucketCfg ReallyBadRolls = new() { Min = 1, Max = 1 };
        public static BucketCfg BadRolls = new(){ Min=1, Max=2 };
        public static BucketCfg GoodRolls = new(){ Min=1, Max=3 };
        public static BucketCfg JackpotRolls = new(){ Min=2, Max=5 };

        static BucketCfg ReadBucket(XmlDocument doc, string name, int dmin, int dmax)
        {
            var n = doc.SelectSingleNode($"//buckets/bucket[@name='{name}']") as XmlElement;
            var cfg = new BucketCfg{ Min=dmin, Max=dmax };
            if (n != null)
            {
                if (int.TryParse(n.GetAttribute("rolls_min"), out var v)) cfg.Min = v;
                if (int.TryParse(n.GetAttribute("rolls_max"), out var w)) cfg.Max = Math.Max(w, cfg.Min);
            }
            return cfg;
        }


        public static List<LootEntry> Bad = new(), Good = new(), Jackpot = new();
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
                Log.Warning("[DonkCasinoSlots] Failed to load casino_loot.xml, using defaults. " + e);
            }
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
    }
}
