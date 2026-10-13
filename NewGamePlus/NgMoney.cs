using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // MONEY DROPS - stage 2 (built 2026-10-06, his go: "lets move on to build
    // whatevers left of stage 2"). DECIDED (CONCEPT.md 2.5 and section 7):
    // opt-in, OFF by default; MORE PICKUPS, using the game's own 10 / 20 / 50;
    // +25% a plus; no default cap; every source pays, each with an on/off.
    //
    // ★ ONE SEAM FOR EVERY SOURCE. Measured: every drop the game rolls goes
    // through `LootSelector.SelectLoot(DropTable, seed)` - an enemy's or a
    // crate's `LootDropper`, the Wheat plant's, and the gold in the rock
    // (`LevelSegmentComponent`, a cell's own drop table). So one postfix there
    // adds money pickups, and the source is read off the TABLE's name
    // (NgMath.MoneySource - 44 tables name a money pickup: 34 enemies, the
    // Queen, 7 crates and boxes, gold, wheat).
    //
    // ★ THE FRACTION IS A CHANCE (my call, not objected to): a pile of 3 at
    // +25% is 3 and a 75% chance of a fourth (NgMath.MoneyExtra). The extras
    // are copies of the pile's own pickups, so the 10 / 20 / 50 mix holds.
    // The roll is seeded from the drop's own seed - the same drop, the same
    // answer.
    //
    // Money a Weapon Forge or Module Forge kill reward pays is spawned by that
    // mod, not rolled from a drop table, so it is never scaled here.
    internal static class NgMoney
    {
        // Extra money pickups this world (WORLD INFO).
        internal static int Extra;

        // A money pickup is a prefab whose ResourcePickup pays the score
        // resource (GameController.scoreResource is Resource Money). Asked once
        // per prefab.
        private static readonly Dictionary<int, bool> _isMoney = new Dictionary<int, bool>();
        private static bool _saidFirst;
        private static bool _saidError;

        internal static void Reset()
        {
            Extra = 0;
            BigCoins = 0;
            _saidFirst = false;
            _saidError = false;
            _saidBig = false;
            // _isMoney, _amount and the big-coin templates are about prefabs,
            // not a run: kept.
        }

        internal static bool IsMoney(DroppabbleItem item)
        {
            if (item.droppableType != DroppabbleType.Prefab || item.prefab == null)
                return false;

            int id = item.prefab.GetInstanceID();
            bool money;

            if (_isMoney.TryGetValue(id, out money))
                return money;

            ResourcePickup p = item.prefab.GetComponent<ResourcePickup>();
            money = p != null && p.resource != null && p.resource.name == "Resource Money";
            _isMoney[id] = money;
            return money;
        }

        internal static IEnumerable<DroppabbleItem> More(DropTable table, int seed, IEnumerable<DroppabbleItem> drops)
        {
            if (!NgRun.Live || table == null || drops == null)
                return drops;

            NgMoneyRules m = NgRun.Rules.moneyDrops;
            float pct = m.PercentAt(NgRun.RulesPlus);

            if (pct <= 0f)
                return drops;

            int source = NgMath.MoneySource(table.name);

            if (!m.Pays(source))
                return drops;

            var list = new List<DroppabbleItem>(drops);
            var money = new List<DroppabbleItem>();

            foreach (DroppabbleItem d in list)
                if (IsMoney(d))
                    money.Add(d);

            if (money.Count == 0)
                return list;

            var rnd = new System.Random(seed ^ 0x4E47504D);
            int extra = NgMath.MoneyExtra(money.Count, pct, rnd.NextDouble());

            // ★ COPIES ONLY UP TO THE PILE'S OWN SIZE (R21 part 4, 356): money
            // pickups collide with each other and are all made at one point,
            // so 600 of them froze the game for seconds and the Queen's for
            // minutes. Past the cap the money comes as BIG COINS.
            int copies = Math.Min(extra, NgMath.MoneyCopyCap(money.Count));

            for (int i = 0; i < copies; i++)
                list.Add(money[i % money.Count]);

            int bigMade = 0;
            int bigValue = 0;

            if (extra > copies)
            {
                // The money the missing copies would have carried, exactly.
                int value = 0;

                for (int i = copies; i < extra; i++)
                    value += AmountOf(money[i % money.Count]);

                int rest;
                List<int> coins = NgMath.BigCoins(value, out rest);

                foreach (int c in coins)
                {
                    GameObject big = BigCoin(money, c);

                    if (big == null)
                    {
                        // No template: pay it in the pile's own coins after all,
                        // capped - the money is short rather than the game frozen.
                        rest += c;
                        continue;
                    }

                    var item = new DroppabbleItem();
                    item.droppableType = DroppabbleType.Prefab;
                    item.prefab = big;
                    list.Add(item);
                    bigMade++;
                    bigValue += c;
                }

                // Under 100 left (or a big coin that could not be made): the
                // pile's smallest coin, never more than MaxMoneyCopies of them.
                DroppabbleItem small = Smallest(money);
                int each = Math.Max(1, AmountOf(small));
                int n = Math.Min(NgMath.MaxMoneyCopies, (rest + each - 1) / each);

                for (int i = 0; i < n; i++)
                    list.Add(small);

                copies += n;
            }

            Extra += copies;
            BigCoins += bigMade;

            if (extra > 0 && !_saidFirst)
            {
                _saidFirst = true;
                NewGamePlusPlugin.Log.LogInfo(
                    "money drops " + NgMath.Pct(pct) + " (" + NgMath.WorldName(NgRun.Plus) + ", " +
                    m.SourcesText() + "): the first extra money - " + table.name + " (" +
                    NgMath.MoneySourceNames[source] + ") dropped " + money.Count + " money pickup(s) and " +
                    copies + " more" + (bigMade > 0 ? ", plus " + bigMade + " BIG COIN(S) worth " + bigValue : "") +
                    ". WORLD INFO counts them. Said once a world.");
            }

            if (bigMade > 0 && !_saidBig)
            {
                _saidBig = true;
                NewGamePlusPlugin.Log.LogInfo(
                    "money drops: " + table.name + " would have dropped " + (money.Count + extra) +
                    " money pickups, so the money past " + (money.Count + NgMath.MoneyCopyCap(money.Count)) +
                    " came as " + bigMade + " big coin(s) worth " + bigValue + " - money pickups bump into " +
                    "each other, and hundreds made at one spot freeze the game. Said once a world.");
            }

            return list;
        }

        // Big coins this world (WORLD INFO).
        internal static int BigCoins;
        private static bool _saidBig;
        private static bool _saidNoTemplate;

        // A money prefab's own amount (10 / 20 / 50), asked once per prefab.
        private static readonly Dictionary<int, int> _amount = new Dictionary<int, int>();

        private static int AmountOf(DroppabbleItem item)
        {
            if (item.prefab == null)
                return 0;

            int id = item.prefab.GetInstanceID();
            int a;

            if (_amount.TryGetValue(id, out a))
                return a;

            ResourcePickup p = item.prefab.GetComponent<ResourcePickup>();
            a = p != null ? Mathf.Max(0, Mathf.RoundToInt(p.amount)) : 0;
            _amount[id] = a;
            return a;
        }

        private static DroppabbleItem Smallest(List<DroppabbleItem> money)
        {
            DroppabbleItem best = money[0];

            foreach (DroppabbleItem d in money)
                if (AmountOf(d) < AmountOf(best))
                    best = d;

            return best;
        }

        // ★ A BIG COIN IS THE GAME'S OWN 50, worth more and drawn bigger: a
        // copy made once per value under an inactive holder that outlives the
        // scene (a money pickup is a plain prefab, no saved entity - the loot
        // factory just Instantiates it - so nothing about the save sees it).
        private static Transform _holder;
        private static readonly Dictionary<int, GameObject> _big = new Dictionary<int, GameObject>();
        private static GameObject _template;

        private static GameObject BigCoin(List<DroppabbleItem> pile, int amount)
        {
            GameObject made;

            if (_big.TryGetValue(amount, out made) && made != null)
                return made;

            GameObject template = Template(pile);

            if (template == null)
            {
                if (!_saidNoTemplate)
                {
                    _saidNoTemplate = true;
                    NewGamePlusPlugin.Log.LogWarning(
                        "money drops: no money pickup to copy a big coin from, so the money past the " +
                        "copies is paid in at most " + NgMath.MaxMoneyCopies + " more small coins. Said once.");
                }

                return null;
            }

            if (_holder == null)
            {
                var h = new GameObject("NG+ big coins (templates)");
                h.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(h);
                _holder = h.transform;
            }

            made = UnityEngine.Object.Instantiate(template, _holder);
            made.name = "NG+ Big Coin " + amount;

            ResourcePickup rp = made.GetComponent<ResourcePickup>();

            if (rp != null)
                rp.amount = amount;

            made.transform.localScale = template.transform.localScale * NgMath.BigCoinScale(amount);

            _big[amount] = made;
            return made;
        }

        // The game's biggest money pickup (its 50), else the pile's biggest.
        private static GameObject Template(List<DroppabbleItem> pile)
        {
            if (_template != null)
                return _template;

            try
            {
                foreach (ResourcePickup p in Resources.FindObjectsOfTypeAll<ResourcePickup>())
                {
                    if (p == null || p.gameObject.scene.IsValid())
                        continue;   // a live pickup, not the prefab

                    if (p.resource == null || p.resource.name != "Resource Money")
                        continue;

                    if (_template == null || p.amount > _template.GetComponent<ResourcePickup>().amount)
                        _template = p.gameObject;
                }
            }
            catch (Exception)
            {
                // Fall back to the pile.
            }

            if (_template == null)
            {
                foreach (DroppabbleItem d in pile)
                    if (d.prefab != null && (_template == null || AmountOf(d) > AmountOf(Pick(_template))))
                        _template = d.prefab;
            }

            return _template;
        }

        private static DroppabbleItem Pick(GameObject prefab)
        {
            var d = new DroppabbleItem();
            d.droppableType = DroppabbleType.Prefab;
            d.prefab = prefab;
            return d;
        }

        internal static void SayError(Exception ex)
        {
            if (_saidError)
                return;

            _saidError = true;
            NewGamePlusPlugin.Log.LogError(
                "money drops: a drop could not be given its extra money (" + ex.GetType().Name + ": " +
                ex.Message + ") - it dropped what the game rolled. Said once a world.");
        }
    }

    [HarmonyPatch(typeof(LootSelector), "SelectLoot")]
    internal static class NgMoneyPatch
    {
        static void Postfix(DropTable dropTable, int seed, ref IEnumerable<DroppabbleItem> __result)
        {
            try
            {
                __result = NgMoney.More(dropTable, seed, __result);
            }
            catch (Exception ex)
            {
                NgMoney.SayError(ex);
            }
        }
    }
}
