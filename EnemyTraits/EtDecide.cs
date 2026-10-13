using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // WHICH ENEMY GETS WHICH TRAIT (CONCEPT.md 3.3, 3.6).
    //
    // ★ TRAITS BELONG TO THE ENEMY UNIT, NEVER TO THE WEAPON ASSET. One
    // WeaponData (and one ammo prefab) is shared by every enemy of a kind, so
    // a trait keyed on the asset would reach every one of them. A trait is a
    // decision about one enemy, kept here by instance id and carried on its
    // body by an EtUnit.
    //
    // ★ THE OWNER TRAP - a copy of New Game Plus's two-frame queue. Every
    // path that makes a drone of yours (the game's gadget, Weapon Forge's
    // swarm and wingman bodies) Adds it BEFORE SetOwner, so at Add it looks
    // exactly like a wild enemy. An added enemy is decided two frames later,
    // and anything owned by a ship at any depth is left alone.
    //
    // ★ THE SAME ENEMY KEEPS THE SAME TRAITS with nothing stored per enemy:
    // each trait is covered = roll(seed, instance id, its salt) < its share,
    // and the limit's pick is seeded the same way. A save keeps the ids, the
    // record keeps the seed.
    internal static class EtDecide
    {
        private struct Pending
        {
            public EntityData entity;
            public int frame;
        }

        internal sealed class Picked
        {
            internal readonly bool[] has = new bool[EtMath.TraitCount];
            internal bool partial;     // got a trait not every enemy gets (the marker)

            internal bool Any
            {
                get
                {
                    for (int t = 0; t < has.Length; t++)
                        if (has[t])
                            return true;

                    return false;
                }
            }
        }

        private static readonly Queue<Pending> _queue = new Queue<Pending>();
        private static readonly Dictionary<int, Picked> _byId = new Dictionary<int, Picked>();

        private static readonly HashSet<EntityData> _destroyed = new HashSet<EntityData>();
        private static readonly Action<EntityData> MarkDestroyed = d => { if (d != null) _destroyed.Add(d); };

        private static readonly Dictionary<string, int> _failed = new Dictionary<string, int>();

        // Counted per world, for the report and WORLD INFO.
        private static int _decided, _withTraits, _owned, _gone, _limited, _sinceReport;
        private static readonly int[] _perTrait = new int[EtMath.TraitCount];
        private static float _reportAfter = -1f;

        internal static void Reset()
        {
            foreach (Pending p in _queue)
                if (p.entity != null)
                    p.entity.Destroyed -= MarkDestroyed;

            _queue.Clear();
            _byId.Clear();
            _destroyed.Clear();
            _failed.Clear();
            _decided = _withTraits = _owned = _gone = _limited = _sinceReport = 0;

            for (int t = 0; t < _perTrait.Length; t++)
                _perTrait[t] = 0;

            _reportAfter = -1f;
        }

        internal static int Decided { get { return _decided; } }
        internal static int WithTraits { get { return _withTraits; } }
        internal static int Limited { get { return _limited; } }

        internal static int CountOf(int trait)
        {
            return trait >= 0 && trait < _perTrait.Length ? _perTrait[trait] : 0;
        }

        internal static Picked Get(int instanceId)
        {
            Picked p;
            return _byId.TryGetValue(instanceId, out p) ? p : null;
        }

        // ---- who is queued -----------------------------------------------------

        // EntityManager.Add, postfix: a NEW entity of this world.
        internal static void OnAdded(EntityData e)
        {
            if (e == null || EtRun.Loading || !EtRun.Live)
                return;

            // The enemy test first: it is two lookups, and every entity in the
            // game (plants, pickups...) passes through here.
            if (IsEnemy(e) && EtRun.Now.Any)
                Enqueue(e);
        }

        // GameSaver.Load, postfix: every enemy the save brought back is decided
        // again - the same roll, so the same traits.
        internal static void OnLoaded()
        {
            if (!EtRun.Live || !EtRun.Now.Any)
                return;

            EntityManager em;

            if (!ServiceLocator.TryGet<EntityManager>(out em) || em == null)
                return;

            int n = 0;

            foreach (EntityData e in em.GetAllEntities())
            {
                if (e != null && IsEnemy(e))
                {
                    Enqueue(e);
                    n++;
                }
            }

            EnemyTraitsPlugin.Log.LogInfo(n + " saved enemies are being given their traits again (the same ones as before).");
        }

        private static void Enqueue(EntityData e)
        {
            e.Destroyed += MarkDestroyed;
            _queue.Enqueue(new Pending { entity = e, frame = Time.frameCount });
        }

        private static bool IsEnemy(EntityData e)
        {
            Enemy.Data enemy;
            AIAgent.Data agent;

            // Crates and boxes carry Enemy too; none has an AIAgent.
            return e.TryGetComponent<Enemy.Data>(out enemy) && e.TryGetComponent<AIAgent.Data>(out agent);
        }

        // Owned by a player ship at any depth: a drone, a drone's drone.
        private static bool OwnedByAShip(Unit.Data unit)
        {
            EntityData owner = unit.Owner;

            for (int depth = 0; owner != null && depth < 8; depth++)
            {
                if (owner.entityId == "Ship")
                    return true;

                Unit.Data next;

                if (!owner.TryGetComponent<Unit.Data>(out next) || next == null)
                    return false;

                owner = next.Owner;
            }

            return false;
        }

        // ---- the decision ------------------------------------------------------------

        internal static void Tick()
        {
            if (_queue.Count == 0)
            {
                if (_reportAfter > 0f && Time.unscaledTime >= _reportAfter)
                    Report();

                return;
            }

            int now = Time.frameCount;
            int budget = 400;

            while (_queue.Count > 0 && budget-- > 0)
            {
                Pending p = _queue.Peek();

                if (p.frame > now - 2)
                    break;

                _queue.Dequeue();

                if (p.entity == null)
                    continue;

                p.entity.Destroyed -= MarkDestroyed;

                if (_destroyed.Remove(p.entity))
                {
                    _gone++;
                    _sinceReport++;
                    continue;
                }

                try
                {
                    Decide(p.entity);
                }
                catch (Exception ex)
                {
                    string kind = p.entity.entityId ?? "?";
                    int n;

                    _failed.TryGetValue(kind, out n);
                    _failed[kind] = n + 1;
                    _sinceReport++;

                    if (n == 0)
                        EnemyTraitsPlugin.Log.LogWarning(
                            "could not give enemy " + kind + " its traits - it stays as it is. Said once per kind; " +
                            "the report counts the rest. What went wrong: " + ex);
                }
            }

            _reportAfter = Time.unscaledTime + 2f;
        }

        private static void Decide(EntityData e)
        {
            Unit.Data unit;

            if (!e.TryGetComponent<Unit.Data>(out unit) || unit == null)
                return;

            if (OwnedByAShip(unit))
            {
                _owned++;
                _sinceReport++;
                return;
            }

            EtNow now = EtRun.Now;

            if (!now.Any)
                return;

            int id = e.instanceId, seed = EtRun.Seed;
            var picked = new Picked();

            for (int t = 0; t < EtMath.TraitCount; t++)
            {
                float c = now.coverage[t];

                if (c <= 0f || !EtMath.Covered(seed, id, EtMath.CoverSalt(t), c))
                    continue;

                picked.has[t] = true;

                if (c < 100f)
                    picked.partial = true;
            }

            // The optional limit (off by default): keep that many, by seed.
            if (now.limit >= 0 && EtMath.ApplyLimit(picked.has, now.limit, seed, id) > 0)
            {
                picked.partial = true;
                _limited++;
            }

            _decided++;
            _sinceReport++;

            if (!picked.Any)
                return;

            _byId[id] = picked;
            _withTraits++;

            for (int t = 0; t < EtMath.TraitCount; t++)
                if (picked.has[t])
                    _perTrait[t]++;

            // The body, if it is already in the world. One that is not (its
            // stretch of the world is not loaded) gets its traits when it
            // appears (OnSpawned).
            EntityGameObjectManager manager;
            SavableEntity se;

            if (ServiceLocator.TryGet<EntityGameObjectManager>(out manager) && manager != null &&
                manager.TryGetSavableEntity(id, out se) && se != null)
            {
                Dress(se, picked);
            }
        }

        // EntityGameObjectManager.SpawnObjectForEntity, postfix: an enemy's
        // body appears (made in play, or its stretch of the world loaded). A
        // body is a NEW object each time it loads, so it is dressed again.
        internal static void OnSpawned(SavableEntity se)
        {
            if (se == null || se.EntityData == null || _byId.Count == 0)
                return;

            Picked p;

            if (_byId.TryGetValue(se.EntityData.instanceId, out p))
                Dress(se, p);
        }

        private static void Dress(SavableEntity se, Picked p)
        {
            EtUnit u = se.GetComponent<EtUnit>();

            if (u == null)
                u = se.gameObject.AddComponent<EtUnit>();

            u.Set(se.EntityData.instanceId, p);

            if (p.partial)
                EtMarker.MaybeAttach(se);
        }

        private static void Report()
        {
            _reportAfter = -1f;

            if (_sinceReport == 0)
                return;

            _sinceReport = 0;

            var each = new List<string>();

            for (int t = 0; t < EtMath.TraitCount; t++)
                if (_perTrait[t] > 0)
                    each.Add(EtMath.Names[t] + " " + _perTrait[t]);

            string failed = "";

            if (_failed.Count > 0)
            {
                var parts = new List<string>();

                foreach (KeyValuePair<string, int> kv in _failed)
                    parts.Add(kv.Key + " x" + kv.Value);

                failed = ", could not give traits to " + string.Join(", ", parts.ToArray()) +
                         " (the first of each kind is in the log above)";
            }

            EnemyTraitsPlugin.Log.LogInfo(
                "enemy traits (" + EtMath.WorldName(EtRun.Now.plus) + "): " + _decided + " enemies decided so far, " +
                _withTraits + " with traits (" + (each.Count > 0 ? string.Join(", ", each.ToArray()) : "none") + ")" +
                (_limited > 0 ? ", " + _limited + " cut down by the limit" : "") +
                ", " + _owned + " yours (never given traits)" +
                (_gone > 0 ? ", " + _gone + " destroyed before their turn" : "") + failed + ".");
        }
    }

    // ---- the patches ------------------------------------------------------------

    [HarmonyPatch(typeof(EntityManager), "Add")]
    internal static class EtAddPatch
    {
        static void Postfix(EntityData entity)
        {
            try
            {
                EtDecide.OnAdded(entity);
            }
            catch (Exception)
            {
                // Called for every entity in the game; it must never throw.
            }
        }
    }

    [HarmonyPatch(typeof(EntityGameObjectManager), "SpawnObjectForEntity")]
    internal static class EtSpawnPatch
    {
        static void Postfix(SavableEntity __result)
        {
            try
            {
                EtDecide.OnSpawned(__result);
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits could not dress an enemy that appeared: " + ex);
            }
        }
    }
}
