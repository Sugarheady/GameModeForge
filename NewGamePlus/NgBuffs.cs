using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // THE TWO NUMBER BUFFS OF STAGE 1: enemy health and enemy count
    // (CONCEPT.md 2.5), bosses included (DECIDED: "the player is going to be
    // strong in the new game, might as well make everything strong too").
    //
    // ---- ENEMY COUNT - the spawn budget -------------------------------------
    //
    // `EnemyGenerator.PlaceBasedOnEcosystem` computes
    // `remainingPowerLevel = radius * difficulty * difficultyMultiplier` and
    // spends it drawing enemies, so scaling `difficultyMultiplier` means more
    // enemies per room (and bigger ones, since a draw may not exceed what is
    // left). World-wide, so no coverage. Two measured limits, said in the docs:
    //   - it can never add a Queen: the boss rooms' PoI has multiplier 0, and
    //     `PlaceEnemies` skips a room whose multiplier is not above 0;
    //   - a room stops at the first enemy it cannot find a free spot for in 5
    //     tries, so +30% is a budget, not a head count.
    //
    // ---- ENEMY HEALTH - the game's own dial ---------------------------------
    //
    // An enemy's health is a `ModifyResourceCapacity` on its embedded module.
    // Co-op already multiplies it (`Enemy.Initialize`, and again on load in
    // `ModuleGridOwner.Data.RestoreFromMemento`); NG+ multiplies on top.
    //
    // ★ THE ID TRAP (measured 2026-09-29). `Enemy.Initialize` runs inside
    // `CreateData()`, BEFORE the generator gives the enemy its instance id - so
    // a coverage roll there would give every enemy the same answer. NG+ acts
    // once the entity is ADDED (its id is set by then).
    //
    // ★ AND NOT EVEN THEN: A FRAME OR TWO LATER. The player's own drones and
    // minions ARE enemy prefabs (Enemy + AIAgent), and every path that makes
    // one - the game's gadget, Weapon Forge's swarm and wingman bodies - calls
    // `CreateEntity` (which Adds it) BEFORE `SetOwner`. At Add a drone looks
    // exactly like a wild enemy. So an added enemy is queued, and decided two
    // frames later, when its owner is known: anything owned (at any depth) by
    // a ship is left alone. A wild enemy is out of reach that long anyway.
    //
    // ★ HEALTH IS WHAT THE ENEMY TAKES DAMAGE THROUGH - not "Resource Health".
    // Measured on the embedded modules: the drones take damage through Caps,
    // Electron or Purple, the Crawler through Tech, and several enemies also
    // carry White / Fuel / Caps tanks that are ammo. Co-op scales every one of
    // them; NG+ scales only the resource the prefab's `DamagableResource` uses.
    //
    // ★ CRATES ARE NOT ENEMIES. Boxes and crates carry `Enemy` too; the one
    // thing none of them has is an `AIAgent` (Weapon Forge measured 60 of 60
    // combatants with one, 0 of 13 containers). Both are required here.
    //
    // ★ THE SAME ENEMY KEEPS THE SAME BUFF, with nothing stored per enemy
    // (2.6): covered = roll(NG+ seed, instance id) < coverage. A save keeps the
    // ids, NG+'s record keeps the seed.
    internal static class NgBuffs
    {
        private const int SaltHealth = 1;

        private struct Pending
        {
            public EntityData entity;
            public bool fresh;      // made this world (fill the new health) vs restored by a load
            public int frame;
        }

        private static readonly Queue<Pending> _queue = new Queue<Pending>();

        // Which enemies carry a buff this world - the marker and WORLD INFO.
        private static readonly HashSet<int> _buffed = new HashSet<int>();

        // The module instances already multiplied. A restored enemy gets a NEW
        // module instance (Module.Memento.Restore deep-copies from the asset),
        // which is not in here, so it is multiplied again - exactly once.
        private static ConditionalWeakTable<Module, object> _done =
            new ConditionalWeakTable<Module, object>();

        private static readonly object Marked = new object();

        // entityId -> the resource that enemy takes damage through (null =
        // none found, which is said once and then skipped).
        private static readonly Dictionary<string, Resource> _damageResource =
            new Dictionary<string, Resource>();

        private static readonly HashSet<string> _saidNoHealth = new HashSet<string>();

        // Counted per world, reported once the first wave has been decided.
        private static int _applied, _uncovered, _owned, _noHealth, _gone;
        private static int _sinceReport;
        private static float _reportAfter = -1f;
        private static bool _saidCount;

        // ★ AN ENEMY CAN DIE IN THE TWO FRAMES IT WAITS (R21: 37 warnings of
        // "could not buff enemy Enemy_Child (NullReferenceException)", all
        // around the Queen fights). Enemy_Child is the Cross Queen's minion -
        // `Weapon_Minion_CrossQueen` spawns them in bursts, often straight
        // into the player's fire - and its neighbours in the same bursts WERE
        // buffed, so the type is not the problem. One destroyed before its
        // turn has had every component's OnDestroy run, and recalculating its
        // stats runs handlers that belong to objects already gone. So an
        // enemy destroyed while queued is skipped and counted, never touched.
        private static readonly HashSet<EntityData> _destroyed = new HashSet<EntityData>();
        private static readonly Action<EntityData> MarkDestroyed = d => { if (d != null) _destroyed.Add(d); };

        // Anything else that throws: the FIRST failure of each enemy kind is
        // logged in full (with where it threw), the rest only counted, so one
        // kind cannot fill the log - and the next report names them.
        private static readonly Dictionary<string, int> _failed = new Dictionary<string, int>();

        internal static void Reset()
        {
            foreach (Pending p in _queue)
                if (p.entity != null)
                    p.entity.Destroyed -= MarkDestroyed;

            _queue.Clear();
            _buffed.Clear();
            _done = new ConditionalWeakTable<Module, object>();
            _saidNoHealth.Clear();
            _destroyed.Clear();
            _failed.Clear();
            _applied = _uncovered = _owned = _noHealth = _gone = _sinceReport = 0;
            _reportAfter = -1f;
            _saidCount = false;

            // The prefab lookup is a fact about the game's assets, not about
            // a run, but it holds Resource references from a registry that is
            // rebuilt with the scene - cheap to redo.
            _damageResource.Clear();
        }

        internal static bool IsBuffed(int instanceId)
        {
            return _buffed.Contains(instanceId);
        }

        internal static int BuffedCount
        {
            get { return _buffed.Count; }
        }

        // ---- enemy count ----------------------------------------------------

        internal static void ScaleBudget(ref float difficultyMultiplier)
        {
            float pct = NgRun.CountPercent;

            if (pct <= 0f)
                return;

            difficultyMultiplier *= NgMath.Multiplier(pct);

            if (!_saidCount)
            {
                _saidCount = true;
                NewGamePlusPlugin.Log.LogInfo(
                    "enemy count " + NgMath.Pct(pct) + ": every room's spawn budget is x" +
                    NgMath.Multiplier(pct).ToString("0.##") + " in " + NgMath.WorldName(NgRun.Plus) +
                    " (a budget, not a head count - a crowded room stops early; boss rooms are " +
                    "never given ecosystem enemies, so there are still four Queens).");
            }
        }

        // ---- enemy health ---------------------------------------------------

        // EntityManager.Add, postfix: a NEW entity of this world.
        internal static void OnAdded(EntityData e)
        {
            // Enemy weapon stats (stage 2) are decided at the same moment, for
            // the same reason (the owner trap), so the queue runs for either.
            if (e == null || NgRun.Loading || (!Wanted() && !NgWeapons.Wanted()))
                return;

            if (IsEnemy(e))
                Enqueue(new Pending { entity = e, fresh = true, frame = Time.frameCount });
        }

        private static void Enqueue(Pending p)
        {
            p.entity.Destroyed += MarkDestroyed;
            _queue.Enqueue(p);
        }

        // GameSaver.Load, postfix: every enemy the save brought back. Their
        // modules were rebuilt from the save, so each is multiplied again -
        // and keeps the health it was saved with.
        internal static void OnLoaded()
        {
            if (!Wanted() && !NgWeapons.Wanted())
                return;

            EntityManager em;

            if (!ServiceLocator.TryGet<EntityManager>(out em) || em == null)
                return;

            int n = 0;

            foreach (EntityData e in em.GetAllEntities())
            {
                if (e != null && IsEnemy(e))
                {
                    Enqueue(new Pending { entity = e, fresh = false, frame = Time.frameCount });
                    n++;
                }
            }

            NewGamePlusPlugin.Log.LogInfo(
                (Wanted() ? "enemy health " + NgMath.Pct(NgRun.HealthPercent) : "enemy weapons") + ": " + n +
                " saved enemies are being re-buffed from the save's rules (the same ones as before).");
        }

        private static bool Wanted()
        {
            return NgRun.HealthPercent > 0f && NgRun.HealthCoverage > 0f;
        }

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

                // Two frames: late enough for SetOwner, whatever made the unit.
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
                    Apply(p);
                }
                catch (Exception ex)
                {
                    // One enemy that cannot be buffed stays as the game made it.
                    string kind = p.entity.entityId ?? "?";
                    int n;

                    _failed.TryGetValue(kind, out n);
                    _failed[kind] = n + 1;
                    _sinceReport++;

                    if (n == 0)
                    {
                        NewGamePlusPlugin.Log.LogWarning(
                            "could not buff enemy " + kind + " - it keeps its normal health. Said once " +
                            "per kind of enemy; the report counts the rest. What went wrong: " + ex);
                    }
                }
            }

            _reportAfter = Time.unscaledTime + 2f;
        }

        private static void Apply(Pending p)
        {
            EntityData e = p.entity;

            Unit.Data unit;
            ModuleGridOwner.Data grid;

            if (!e.TryGetComponent<Unit.Data>(out unit) || unit == null ||
                !e.TryGetComponent<ModuleGridOwner.Data>(out grid) || grid == null ||
                grid.ModuleGrid == null)
            {
                return;
            }

            if (OwnedByAShip(unit))
            {
                _owned++;
                _sinceReport++;
                return;
            }

            // Its guns: every weapon stat rolls on its own (NgWeapons).
            NgWeapons.Decide(e);

            if (!Wanted())
                return;

            if (!NgMath.Covered(NgRun.Seed, e.instanceId, SaltHealth, NgRun.HealthCoverage))
            {
                _uncovered++;
                _sinceReport++;
                return;
            }

            Resource res = DamageResourceOf(e.entityId);

            if (res == null)
            {
                _noHealth++;
                _sinceReport++;

                if (_saidNoHealth.Add(e.entityId ?? "?"))
                {
                    NewGamePlusPlugin.Log.LogWarning(
                        "enemy '" + e.entityId + "' has no resource it takes damage through that " +
                        "New Game Plus can find - it is left at normal health.");
                }

                return;
            }

            IModuleCluster passive = grid.ModuleGrid.GetCluster(ClusterType.Passive);

            if (passive == null || !passive.HasMainModule || passive.MainModule == null)
                return;

            Module module = passive.MainModule;
            object seen;

            if (_done.TryGetValue(module, out seen))
            {
                _buffed.Add(e.instanceId);
                return;
            }

            float mult = NgMath.Multiplier(NgRun.HealthPercent);
            bool touched = false;

            foreach (ModuleEffect fx in module.Effects)
            {
                ModifyResourceCapacity cap = fx as ModifyResourceCapacity;

                if (cap == null || cap.resource != res)
                    continue;

                // The whole series, not just its first term - a level-2 enemy
                // must be as much tougher as a level-1 one. (`delta` is a
                // struct field of a class, so this writes the real value.)
                cap.delta.baseValue *= mult;

                if (cap.delta.increaseMethod == FloatSeries.IncreaseMethod.Add)
                    cap.delta.change *= mult;

                touched = true;
            }

            if (!touched)
            {
                _noHealth++;
                _sinceReport++;
                return;
            }

            _done.Add(module, Marked);

            ResourceTank tank = unit.GetTank(res);
            float oldCap = tank != null ? tank.Capacity : 0f;
            float oldValue = tank != null ? tank.Value : 0f;

            unit.RecalculateStats(grid.ModuleGrid);

            tank = unit.GetTank(res);

            // A new enemy was made full, so it stays full at the new size. A
            // restored one keeps the health it was saved on - which, for an
            // enemy already buffed last session, only fits under the new cap.
            if (p.fresh && tank != null && oldValue >= oldCap - 0.01f)
                tank.Value = tank.Capacity;

            _buffed.Add(e.instanceId);
            _applied++;
            _sinceReport++;

            NgMarker.MaybeAttach(e.instanceId);
        }

        private static void Report()
        {
            _reportAfter = -1f;

            if (_sinceReport == 0 || !Wanted())
                return;

            _sinceReport = 0;

            NewGamePlusPlugin.Log.LogInfo(
                "enemy health " + NgMath.Pct(NgRun.HealthPercent) + " on " +
                Math.Round(NgRun.HealthCoverage) + "% of enemies (" + NgMath.WorldName(NgRun.Plus) +
                "): " + _applied + " buffed so far, " + _uncovered + " not in the covered share, " +
                _owned + " yours (drones and minions are never buffed)" +
                (_noHealth > 0 ? ", " + _noHealth + " with no health New Game Plus could find" : "") +
                (_gone > 0 ? ", " + _gone + " destroyed before their turn (left alone)" : "") +
                Failed() + ".");
        }

        private static string Failed()
        {
            if (_failed.Count == 0)
                return "";

            var parts = new List<string>();

            foreach (KeyValuePair<string, int> kv in _failed)
                parts.Add(kv.Key + " x" + kv.Value);

            return ", could not buff " + string.Join(", ", parts.ToArray()) +
                   " (the first of each kind is in the log above)";
        }

        private static bool IsEnemy(EntityData e)
        {
            Enemy.Data enemy;
            AIAgent.Data agent;

            return e.TryGetComponent<Enemy.Data>(out enemy) &&
                   e.TryGetComponent<AIAgent.Data>(out agent);
        }

        // Owned by a player ship at any depth: a drone, a drone's drone.
        // (NgDamage asks the same question of an attacker.)
        internal static bool OwnedByAShip(Unit.Data unit)
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

        private static Resource DamageResourceOf(string entityId)
        {
            Resource found;

            if (entityId == null)
                return null;

            if (_damageResource.TryGetValue(entityId, out found))
                return found;

            found = null;

            try
            {
                SavablesCollection all;

                if (ServiceLocator.TryGet<SavablesCollection>(out all) && all != null)
                {
                    foreach (SavablesCollection.EntityPrefab info in all.savableObjectInfos)
                    {
                        if (info.entityId != entityId || info.prefab == null)
                            continue;

                        DamagableResource d = info.prefab.GetComponentInChildren<DamagableResource>(true);

                        if (d != null)
                            found = d.resource;

                        break;
                    }
                }
            }
            catch (Exception)
            {
                found = null;
            }

            _damageResource[entityId] = found;
            return found;
        }
    }

    // The opt-in marker (DECIDED 2026-09-29: OFF by default) - never a colour
    // tint on the enemy itself, because the game's hit flash owns a unit's
    // colour (Weapon Forge's tint contract). It follows the enemy without
    // turning with it.
    //
    // ★ R21 302: "I dont really see the marked enemies ... Maybe its worth
    // giving the enemies a bubble art (like a super small icon next to their
    // ships)." The first marker was a 3-pixel square, 0.15 units across - a
    // speck. It is his bubble now: an amber ring with a plus in it, 13 pixels
    // (0.65 units, about half a small enemy), beside the enemy's upper right
    // and breathing slowly so the eye finds it.
    internal sealed class NgMarker : MonoBehaviour
    {
        private static Sprite _sprite;

        private const int Size = 13;

        private Transform _target;
        private Vector3 _offset;
        private SpriteRenderer _sr;
        private float _phase;

        internal static void MaybeAttach(int instanceId)
        {
            if (NgSettings.EnemyMarker == null || !NgSettings.EnemyMarker.Value)
                return;

            EntityGameObjectManager manager;
            SavableEntity se;

            if (!ServiceLocator.TryGet<EntityGameObjectManager>(out manager) || manager == null ||
                !manager.TryGetSavableEntity(instanceId, out se) || se == null)
            {
                return;
            }

            Attach(se);
        }

        // EntityGameObjectManager.SpawnObjectForEntity, postfix: an enemy's
        // GameObject appears (its segment loaded) after it was buffed.
        //
        // ★ R21 PART 3, 341: "I got them to show up once when i saved and quit
        // and then continued after. but so far i dont really see them at all."
        // This used to hang off SpawnPrefabForEntity - and the decompile has
        // exactly ONE caller of that: ShipManager, for the SHIPS. Every enemy
        // a segment loads (InstantiateGameObjects) and every one made in play
        // (CreateEntity) comes through SpawnObjectForEntity. So a marker only
        // ever appeared on an enemy whose GameObject happened to exist at the
        // moment it was buffed - the Queen's children, spawned beside you, and
        // the enemies near you on a Continue - and never on one that streamed
        // in as you flew.
        internal static void OnSpawned(SavableEntity se)
        {
            if (se == null || se.EntityData == null)
                return;

            if (NgSettings.EnemyMarker == null || !NgSettings.EnemyMarker.Value)
                return;

            if (NgBuffs.IsBuffed(se.EntityData.instanceId) || NgWeapons.IsMarked(se.EntityData.instanceId))
                Attach(se);
        }

        private static void Attach(SavableEntity se)
        {
            try
            {
                // ★ ONE MARKER (stage 3, CONCEPT.md 3.6): Enemy Traits draws the
                // same bubble under the same child name, so an enemy either DLL
                // has marked is marked once.
                if (se.GetComponentInChildren<NgMarker>(true) != null || se.transform.Find("NgMarker") != null)
                    return;

                if (_sprite == null)
                    _sprite = MakeSprite();

                var go = new GameObject("NgMarker");
                go.transform.SetParent(se.transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.color = new Color(1f, 0.62f, 0.15f, 1f);

                // Draw where the enemy draws, on top of it.
                SpriteRenderer body = se.GetComponentInChildren<SpriteRenderer>();

                if (body != null && body != sr)
                {
                    sr.sortingLayerID = body.sortingLayerID;
                    sr.sortingOrder = body.sortingOrder + 50;
                }

                // Beside its upper right, clear of the body.
                Vector2 ext = new Vector2(0.5f, 0.5f);
                Collider2D col = se.GetComponentInChildren<Collider2D>();

                if (col != null)
                    ext = col.bounds.extents;

                var m = go.AddComponent<NgMarker>();
                m._target = se.transform;
                m._offset = new Vector3(ext.x * 0.8f + 0.3f, ext.y + 0.35f, -0.01f);
                m._sr = sr;
                m._phase = UnityEngine.Random.value * 6.28f;
                m.LateUpdate();
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        // An amber ring with a plus in it, drawn once: a dark rim outside the
        // ring so it reads over bright art as well as dark. White pixels,
        // tinted amber by the renderer's colour; the rim is near-black.
        private static Sprite MakeSprite()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            var px = new Color32[Size * Size];
            float c = (Size - 1) * 0.5f;
            var clear = new Color32(0, 0, 0, 0);
            var ring = new Color32(255, 255, 255, 255);
            var rim = new Color32(40, 24, 8, 255);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    bool plus = (x == (int)c && Mathf.Abs(y - c) <= 2.5f) ||
                                (y == (int)c && Mathf.Abs(x - c) <= 2.5f);

                    Color32 p = clear;

                    if (d > 4.6f && d <= 5.7f)
                        p = ring;
                    else if (d > 5.7f && d <= 6.5f)
                        p = rim;
                    else if (plus)
                        p = ring;

                    px[y * Size + x] = p;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();

            // PPU 20 is the game's own: 13 pixels = 0.65 units.
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 20f);
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            transform.position = _target.position + _offset;
            transform.rotation = Quaternion.identity;

            // A slow breath, so the eye finds it among moving art.
            if (_sr != null)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f + _phase);
                _sr.color = new Color(1f, 0.62f, 0.15f, 0.7f + 0.3f * k);
                transform.localScale = Vector3.one * (0.92f + 0.12f * k);
            }
        }
    }

    // ---- the patches ------------------------------------------------------

    [HarmonyPatch(typeof(EnemyGenerator), "PlaceBasedOnEcosystem")]
    internal static class NgCountPatch
    {
        static void Prefix(ref float difficultyMultiplier)
        {
            try
            {
                NgBuffs.ScaleBudget(ref difficultyMultiplier);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not scale a room's spawn budget: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(EntityManager), "Add")]
    internal static class NgAddPatch
    {
        static void Postfix(EntityData entity)
        {
            try
            {
                NgBuffs.OnAdded(entity);
            }
            catch (Exception)
            {
                // Called for every entity in the game; it must never throw.
            }
        }
    }

    [HarmonyPatch(typeof(EntityGameObjectManager), "SpawnObjectForEntity")]
    internal static class NgSpawnPatch
    {
        static void Postfix(SavableEntity __result)
        {
            try
            {
                NgMarker.OnSpawned(__result);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }

            try
            {
                // Stage 3: a decided enemy's volley patterns, on its new body.
                NgWeapons.OnSpawned(__result);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not speed up an enemy's volleys: " + ex);
            }
        }
    }
}
