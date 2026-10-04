using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MyBox;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // ENEMY DAMAGE % (CONCEPT.md 2.5), moved into stage 1 on 2026-10-02 at his
    // ask: "If you can push out the damage buff to stage one that would be
    // awesome." What he decided:
    //   - every enemy hits harder, bosses included, at EVERYTHING it hits:
    //     your ship, your drones, minions and wingmen, a co-op partner;
    //   - on every enemy, no coverage roll ("This should be a constant for
    //     everything");
    //   - the world's own damage takes the same % on everything it hits, when
    //     `environment` is on (the default) - hazard blocks, the white bulbs,
    //     exploding plants ("increase the damage on them to everything in NGP.
    //     That way its consistent");
    //   - a bullet you knock back keeps its extra damage ("Yes to match");
    //   - nothing of yours is ever touched.
    //
    // ★ ONE MULTIPLY, AT THE DOOR EVERY HIT COMES THROUGH. Every point of
    // damage in the game reaches a HealthBase through one of six entry points
    // (ProjectileCollided, OnHitByHitscanWeapon, OnExplosion, OnHazardTouched,
    // OnHitByElectricity, OnCellCollision), the slime cell's
    // DamageCellBehaviourTarget, or the burn tick in DamagableResource.Update.
    // Each entry decides WHO the hit came from and opens a scope; the victim's
    // TakeDamage (for a burn tick, Damage) scales the damage ONCE and marks the
    // scope used. Scaling at TakeDamage - before the victim's shields - means a
    // harder hit drains a shield harder too. Nothing writes a projectile's
    // Damage, so this composes with everything that does (Weapon Forge's damage
    // stack, crit, a reflect's own multiplier).
    //
    // ★ A HIT WHOSE SOURCE CANNOT BE TOLD IS LEFT ALONE, and said once a world.
    // This mod only ever ADDS difficulty, so every doubt resolves to "add
    // nothing" - the same rule as the rules file.
    //
    // Where each source comes from (measured off the decompile and the prefabs,
    // 2026-10-02):
    //   - a shot: its Owner; with none (a sub-emitter fragment), its layer - a
    //     shot's GameObject layer IS its faction. ★ An enemy's shot is STAMPED
    //     when it is fired, because Weapon Forge's deflect / rally / shield make
    //     a knocked-back bullet the player's (owner, layer and mask).
    //   - a beam: its weapon's Owner, else its target mask (every enemy laser
    //     ships 9280 = Player | Ground | Fruits).
    //   - an explosion: a shot's own blast is classified like the shot. ★ BUT
    //     thirteen ExplosionComponent prefabs explode with NO OWNER (the death
    //     blasts, six ammo blasts, six plant blasts), so each one is classified
    //     as it is MADE - Awake runs synchronously inside Instantiate, so it is
    //     still inside the Die() or the shot's own method that made it - and
    //     that answer is used when it goes off a frame later.
    //   - contact: the Hazard or ChargerHead doing the touching.
    //   - electricity: the conductor the spark came from (Fruit White's, an
    //     enemy's, a shot's), else the electricity system it belongs to.
    //   - a burn tick: ★ it carries no source at all, so each victim keeps who
    //     lit it LAST - a shot's, beam's or blast's ignition (ApplyBurn), or the
    //     steam cell (BurnCellBehaviourTarget, which only the four ships carry).
    //   - the world: a hazard cell, the slime cell, a plant, anything with
    //     health and no unit at all.
    //   - nobody: a crate or box (an Enemy with no AIAgent - Weapon Forge
    //     measured 0 of 13 containers with one) is never made harder and never
    //     counted. His call, 2026-10-02: "I wouldn't count crates as hazards
    //     please. These should contain items for the player to collect." And
    //     measured the same day: none of the 13 has an explosion, a spark
    //     source or a gun - each only spawns a breaking particle and drops loot
    //     (Box Bomb is a box whose drop table holds bombs, not a box that
    //     blows up).
    internal static class NgDamage
    {
        private sealed class Box
        {
            public readonly int side;
            public Box(int s) { side = s; }
        }

        private struct Scope
        {
            public HealthBase victim;   // null: whoever takes the next TakeDamage
            public float mult;
            public bool used;
            public int side;
            public int route;
            public object source;
        }

        // The routes, for the log and for "said once" (bit route * Sides + side).
        private const int Sides = 5;
        internal const int RouteShot = 0, RouteBeam = 1, RouteBlast = 2, RouteContact = 3,
                           RouteSpark = 4, RouteCell = 5, RouteSlime = 6, RouteBurn = 7;

        private static readonly string[] RouteNames =
        {
            "shot", "beam", "blast", "contact hit", "electric spark", "hazard block", "slime", "burn"
        };

        // ---- this world ----------------------------------------------------

        private static float _enemyMult = 1f, _worldMult = 1f;
        internal static bool Active;

        private static int _boosted, _unknown;
        private static long _saidBoost, _saidUnknown;

        internal static int Boosted { get { return _boosted; } }
        internal static int Unknown { get { return _unknown; } }

        // ---- the scopes ----------------------------------------------------

        private static readonly Scope[] _scopes = new Scope[32];
        private static int _depth;

        // The last entry's victim and side, for ApplyBurn - which the game
        // calls right after the base entry returns, inside the same override.
        private static HealthBase _lastVictim;
        private static int _lastSide;

        // Who is making / setting off / touching right now. Each is a stack
        // because they nest: a shot's hit kills its victim, whose Die makes a
        // death blast, inside the shot's own OnObjectHit.
        private static readonly object[] _makers = new object[16];
        private static int _makerDepth;
        private static readonly int[] _blasts = new int[16];
        private static int _blastDepth;
        private static readonly Component[] _hazards = new Component[16];
        private static int _hazardDepth;

        private static ConditionalWeakTable<object, Box> _stamps = new ConditionalWeakTable<object, Box>();
        private static ConditionalWeakTable<DamagableResource, Box> _burns =
            new ConditionalWeakTable<DamagableResource, Box>();
        private static ConditionalWeakTable<ExplosionComponent, Box> _made =
            new ConditionalWeakTable<ExplosionComponent, Box>();

        private static readonly Box EnemyStamp = new Box(NgMath.SideEnemy);
        // Indexed by side, so one per NgMath.Side* constant, in order.
        private static readonly Box[] SideBoxes =
        {
            new Box(NgMath.SideUnknown), new Box(NgMath.SidePlayer),
            new Box(NgMath.SideEnemy), new Box(NgMath.SideWorld), new Box(NgMath.SideNeutral)
        };

        private static Faction _playerFaction;
        private static int _factionTriedFrame = -1;

        private static int _layerEnemyShots = -2, _layerPlayerShots = -2, _layerPlayer = -2, _layerEntities = -2;

        // ★ DECLARED ABOVE THE FIELD REFS ON PURPOSE: static initializers run
        // in the order they are written, and Ref() appends to this when a
        // field is missing. Below them it would still be null there - and the
        // NullReferenceException would take down the whole class with it.
        private static string _missingFields = "";

        // Private game fields, resolved once. A missing one leaves its route
        // inert and is said in the log - it never throws on a hit.
        private static readonly AccessTools.FieldRef<DamagableResource, Unit> UnitOfResource =
            Ref<DamagableResource, Unit>("_unit");
        private static readonly AccessTools.FieldRef<BurnCellBehaviourTarget, bool> SteamBurning =
            Ref<BurnCellBehaviourTarget, bool>("isBurning");
        private static readonly AccessTools.FieldRef<BurnCellBehaviourTarget, Unit> SteamUnit =
            Ref<BurnCellBehaviourTarget, Unit>("unit");
        private static readonly AccessTools.FieldRef<DamageCellBehaviourTarget, HealthBase> SlimeVictim =
            Ref<DamageCellBehaviourTarget, HealthBase>("health");

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string field) where T : class
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(field);
            }
            catch (Exception)
            {
                _missingFields += (_missingFields.Length > 0 ? ", " : "") + typeof(T).Name + "." + field;
                return null;
            }
        }

        // ---- the world starts and ends ------------------------------------

        internal static void Reset()
        {
            Active = false;
            _enemyMult = _worldMult = 1f;
            _boosted = _unknown = 0;
            _saidBoost = _saidUnknown = 0;
            ClearStacks();
            _stamps = new ConditionalWeakTable<object, Box>();
            _burns = new ConditionalWeakTable<DamagableResource, Box>();
            _made = new ConditionalWeakTable<ExplosionComponent, Box>();
            _playerFaction = null;
            _factionTriedFrame = -1;
        }

        private static void ClearStacks()
        {
            for (int i = 0; i < _scopes.Length; i++) _scopes[i] = default(Scope);
            for (int i = 0; i < _makers.Length; i++) _makers[i] = null;
            for (int i = 0; i < _hazards.Length; i++) _hazards[i] = null;
            _depth = _makerDepth = _blastDepth = _hazardDepth = 0;
            _lastVictim = null;
            _lastSide = NgMath.SideUnknown;
        }

        // NgRun.Begin: this world's rules are set.
        internal static void Refresh()
        {
            float pct = NgRun.DamagePercent;
            bool env = NgRun.DamageEnvironment;

            _enemyMult = NgMath.DamageScale(NgMath.SideEnemy, pct, env);
            _worldMult = NgMath.DamageScale(NgMath.SideWorld, pct, env);
            Active = _enemyMult > 1f || _worldMult > 1f;

            if (!Active)
                return;

            NewGamePlusPlugin.Log.LogInfo(
                "enemy damage " + NgMath.Pct(pct) + " in " + NgMath.WorldName(NgRun.Plus) +
                ": every enemy's shots, beams, blasts, contact hits, sparks and fire do x" +
                Num(_enemyMult) + " to everything they hit (bosses included; nothing of yours " +
                "is touched)" +
                (env
                    ? ", and so does the world itself - hazard blocks, white bulbs, exploding " +
                      "plants, steam."
                    : ". The world's own hazards are left as they are (environment off).") +
                (_missingFields.Length > 0
                    ? " (Could not find " + _missingFields + " - those routes are not made harder.)"
                    : ""));
        }

        // The plugin's Update. Every scope is opened and closed inside one call
        // chain, so nothing is open here - unless a game method threw past a
        // finalizer, which this puts right instead of letting it leak.
        internal static void EndFrame()
        {
            if (_depth == 0 && _makerDepth == 0 && _blastDepth == 0 && _hazardDepth == 0)
                return;

            ClearStacks();
        }

        private static float MultFor(int side)
        {
            if (side == NgMath.SideEnemy)
                return _enemyMult;

            if (side == NgMath.SideWorld)
                return _worldMult;

            return 1f;
        }

        // ---- a hit comes in -------------------------------------------------

        // An entry point: who this hit came from. Returns the scope to close,
        // or -1 when nothing will be scaled.
        internal static int Enter(HealthBase victim, int side, int route, object source)
        {
            _lastVictim = victim;
            _lastSide = side;

            if (side == NgMath.SideUnknown)
            {
                SayUnknown(route, source, victim);
                return -1;
            }

            float m = MultFor(side);

            if (m <= 1f)
                return -1;

            return Push(victim, m, side, route, source);
        }

        private static int Push(HealthBase victim, float mult, int side, int route, object source)
        {
            if (_depth >= _scopes.Length)
                return -1;

            _scopes[_depth] = new Scope
            {
                victim = victim, mult = mult, used = false, side = side, route = route, source = source
            };

            return _depth++;
        }

        internal static void Pop(int at)
        {
            if (at < 0 || at >= _scopes.Length)
                return;

            for (int i = at; i < _depth; i++)
                _scopes[i] = default(Scope);

            if (_depth > at)
                _depth = at;
        }

        // The victim's TakeDamage / Damage: the multiplier for this hit, used
        // ONCE per scope (a shielded victim's TakeDamage calls itself again per
        // damage, and Damage(float) follows every TakeDamage).
        internal static float Take(HealthBase victim, out int at)
        {
            at = _depth - 1;

            if (at < 0 || _scopes[at].used)
                return 1f;

            if (_scopes[at].victim != null && !ReferenceEquals(_scopes[at].victim, victim))
                return 1f;

            _scopes[at].used = true;
            return _scopes[at].mult;
        }

        // A hit was made harder: count it, and say the first of each kind.
        internal static void Boost(int at, HealthBase victim, float before, float after)
        {
            _boosted++;

            if (at < 0 || at >= _scopes.Length)
                return;

            int bit = _scopes[at].route * Sides + _scopes[at].side;

            if ((_saidBoost & (1L << bit)) != 0)
                return;

            _saidBoost |= 1L << bit;

            try
            {
                NewGamePlusPlugin.Log.LogInfo(
                    "enemy damage: the first harder " + RouteNames[_scopes[at].route] + " from " +
                    (_scopes[at].side == NgMath.SideWorld ? "the world" : "an enemy") +
                    NameIn(_scopes[at].source) + " on " + NameOf(victim) + ": " + Num(before) +
                    " -> " + Num(after) + ".");
            }
            catch (Exception)
            {
                // A log line is never worth a hit.
            }
        }

        private static void SayUnknown(int route, object source, HealthBase victim)
        {
            _unknown++;

            if ((_saidUnknown & (1L << route)) != 0)
                return;

            _saidUnknown |= 1L << route;

            try
            {
                NewGamePlusPlugin.Log.LogInfo(
                    "enemy damage: a " + RouteNames[route] + NameIn(source) + " on " + NameOf(victim) +
                    " came from something New Game Plus could not tell was an enemy or the world, " +
                    "so it was left at normal damage (said once a world; WORLD INFO counts them).");
            }
            catch (Exception)
            {
            }
        }

        // ---- the burn tick --------------------------------------------------

        // DamagableResource.Update, prefix. The burn tick is the only Damage
        // call in Update, so a scope here reaches exactly it.
        internal static int EnterBurnTick(DamagableResource dr)
        {
            if (UnitOfResource == null)
                return -1;

            // Null on the unit's first frame; Update resolves it itself.
            Unit u = UnitOfResource(dr);

            if (u == null)
                return -1;

            Unit.Data d = u.ComponentData;

            if (d == null || !d.IsOnFire)
                return -1;

            Box b;
            int side = _burns.TryGetValue(dr, out b) ? b.side : NgMath.SideUnknown;
            float m = MultFor(side);

            return m > 1f ? Push(dr, m, side, RouteBurn, null) : -1;
        }

        // DamagableResource.ApplyBurn, prefix: who lit this victim, last.
        internal static void Ignited(DamagableResource dr, MinMaxFloat burn)
        {
            if (burn.Max <= 0f || !ReferenceEquals(_lastVictim, dr))
                return;

            Mark(dr, _lastSide);
        }

        // BurnCellBehaviourTarget.Update, postfix: standing in steam.
        internal static void InSteam(BurnCellBehaviourTarget target)
        {
            if (SteamBurning == null || SteamUnit == null || !SteamBurning(target))
                return;

            Unit u = SteamUnit(target);

            if (u == null)
                return;

            DamagableResource dr = u.GetComponent<DamagableResource>();

            if (dr != null)
                Mark(dr, NgMath.SideWorld);
        }

        private static void Mark(DamagableResource dr, int side)
        {
            Box now;

            if (_burns.TryGetValue(dr, out now))
            {
                if (now.side == side)
                    return;

                _burns.Remove(dr);
            }

            _burns.Add(dr, SideBoxes[side]);
        }

        // ---- the slime cell -------------------------------------------------

        internal static int EnterSlime(DamageCellBehaviourTarget target)
        {
            if (SlimeVictim == null)
                return -1;

            HealthBase victim = SlimeVictim(target);

            return victim != null ? Enter(victim, NgMath.SideWorld, RouteSlime, null) : -1;
        }

        // ---- shots ------------------------------------------------------------

        // Projectile.Shoot / PhysicsProjectile.Shoot, postfix. Re-decided on
        // EVERY Shoot: a lobbed shot's first Shoot is a blank one (no owner
        // yet), and only the last answer may stand.
        internal static void OnShot(object shot)
        {
            IProjectile p = shot as IProjectile;

            if (p == null)
                return;

            _stamps.Remove(shot);

            if (OfShotNow(p) == NgMath.SideEnemy)
                _stamps.Add(shot, EnemyStamp);
        }

        internal static int OfShot(IProjectile p)
        {
            if (p == null)
                return NgMath.SideUnknown;

            Box b;

            // Fired by an enemy - kept through a knock-back.
            if (_stamps.TryGetValue(p, out b))
                return b.side;

            return OfShotNow(p);
        }

        private static int OfShotNow(IProjectile p)
        {
            Unit owner = p.Owner;

            if (owner != null)
                return OfUnit(owner);

            Component c = p as Component;

            if (c == null)
                return NgMath.SideUnknown;

            Layers();
            return NgMath.SideFromShotLayer(c.gameObject.layer, _layerEnemyShots, _layerPlayerShots);
        }

        internal static int OfBeam(HitscanWeapon w)
        {
            if (w == null)
                return NgMath.SideUnknown;

            Unit owner = w.Owner;

            if (owner != null)
                return OfUnit(owner);

            Layers();
            return NgMath.SideFromMask(w.LayerMask.value, _layerPlayer, _layerEntities);
        }

        private static void Layers()
        {
            if (_layerEnemyShots != -2)
                return;

            _layerEnemyShots = LayerMask.NameToLayer("EnemyProjectiles");
            _layerPlayerShots = LayerMask.NameToLayer("PlayerProjectiles");
            _layerPlayer = LayerMask.NameToLayer("Player");
            _layerEntities = LayerMask.NameToLayer("Entities");
        }

        // ---- units ------------------------------------------------------------

        // Yours: a ship, anything a ship owns at any depth (drones, minions,
        // wingman bodies), or anything on the player's side (a charmed enemy).
        // A crate is nobody's. Everything else is an enemy.
        internal static int OfUnit(Unit u)
        {
            if (u == null)
                return NgMath.SideUnknown;

            Unit.Data d = u.ComponentData;
            EntityData e = d != null ? d.entity : null;

            if (e != null && e.entityId == "Ship")
                return NgMath.SidePlayer;

            if (d != null && NgBuffs.OwnedByAShip(d))
                return NgMath.SidePlayer;

            Faction mine = PlayerFaction();
            Faction theirs = u.Faction;

            if (mine != null && theirs != null && (theirs == mine || Contains(mine.Allies, theirs)))
                return NgMath.SidePlayer;

            if (e != null)
            {
                Enemy.Data enemy;
                AIAgent.Data agent;

                if (e.TryGetComponent<Enemy.Data>(out enemy) && !e.TryGetComponent<AIAgent.Data>(out agent))
                    return NgMath.SideNeutral;
            }

            return NgMath.SideEnemy;
        }

        private static bool Contains(IReadOnlyList<Faction> list, Faction f)
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.Count; i++)
                if (list[i] == f)
                    return true;

            return false;
        }

        private static Faction PlayerFaction()
        {
            if (_playerFaction != null)
                return _playerFaction;

            if (_factionTriedFrame == Time.frameCount)
                return null;

            _factionTriedFrame = Time.frameCount;

            try
            {
                ShipManager ships;

                if (ServiceLocator.TryGet<ShipManager>(out ships) && ships != null && ships.Ships != null)
                {
                    foreach (Ship s in ships.Ships)
                    {
                        if (s == null)
                            continue;

                        Unit u = s.GetComponent<Unit>();

                        if (u != null && u.Faction != null)
                        {
                            _playerFaction = u.Faction;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Without it the ownership test still stands.
            }

            return _playerFaction;
        }

        // A thing that is not a unit: a plant's, a unit's, a shot's - or the
        // world's own.
        private static int OfThing(Component c)
        {
            if (c == null)
                return NgMath.SideUnknown;

            if (c.GetComponentInParent<EntityPlantFruit>() != null)
                return NgMath.SideWorld;

            Unit u = c.GetComponentInParent<Unit>();

            if (u != null)
                return OfUnit(u);

            IProjectile p = c.GetComponentInParent<IProjectile>();

            if (p != null)
                return OfShot(p);

            return NgMath.SideWorld;
        }

        internal static int OfHazard()
        {
            return _hazardDepth > 0 ? OfThing(_hazards[_hazardDepth - 1]) : NgMath.SideUnknown;
        }

        internal static int OfSpark(ElectricityConductor c)
        {
            if (c == null)
                return NgMath.SideUnknown;

            // A discharge is a bare GameObject; a unit's, a plant's or a
            // shot's conductor sits on its owner.
            if (c.GetComponentInParent<EntityPlantFruit>() != null)
                return NgMath.SideWorld;

            Unit u = c.GetComponentInParent<Unit>();

            if (u != null)
                return OfUnit(u);

            IProjectile p = c.GetComponentInParent<IProjectile>();

            if (p != null)
                return OfShot(p);

            switch (c.EmittedSystem)
            {
                case ElectricityManager.SubSystemType.Enemy: return NgMath.SideEnemy;
                case ElectricityManager.SubSystemType.Player: return NgMath.SidePlayer;
            }

            Layers();
            return NgMath.SideFromMask(c.LayerMask.value, _layerPlayer, _layerEntities);
        }

        private static int OfDying(HealthBase h)
        {
            if (h == null)
                return NgMath.SideUnknown;

            if (h.GetComponentInParent<EntityPlantFruit>() != null)
                return NgMath.SideWorld;

            Unit u = h.GetComponentInParent<Unit>();

            // Health and no unit: a plant, a prop - the world.
            return u != null ? OfUnit(u) : NgMath.SideWorld;
        }

        // ---- explosions -------------------------------------------------------

        internal static int OfBlast(Explosion e)
        {
            if (_blastDepth > 0)
            {
                int s = _blasts[_blastDepth - 1];

                if (s != NgMath.SideUnknown)
                    return s;
            }

            Unit owner = e.Owner;
            return owner != null ? OfUnit(owner) : NgMath.SideUnknown;
        }

        internal static int PushBlast(int side)
        {
            if (_blastDepth >= _blasts.Length)
                return -1;

            _blasts[_blastDepth] = side;
            return _blastDepth++;
        }

        // A shot's own blast (Projectile / PhysicsProjectile.SpawnExplosion):
        // the shot's side - its stamp first, so a knocked-back enemy rocket's
        // blast is still the enemy's.
        internal static int PushShotBlast(object shot)
        {
            int side;

            try
            {
                side = OfShot(shot as IProjectile);
            }
            catch (Exception)
            {
                side = NgMath.SideUnknown;
            }

            return PushBlast(side);
        }

        internal static void PopBlast(int at)
        {
            if (at >= 0 && _blastDepth > at)
                _blastDepth = at;
        }

        // ExplosionComponent.Awake, postfix - still inside whatever made it.
        internal static void OnMade(ExplosionComponent x)
        {
            int side = NgMath.SideUnknown;

            if (_makerDepth > 0)
            {
                object maker = _makers[_makerDepth - 1];
                HealthBase dying = maker as HealthBase;

                side = dying != null ? OfDying(dying) : OfShot(maker as IProjectile);
            }

            _made.Remove(x);
            _made.Add(x, SideBoxes[side]);
        }

        internal static int MadeSide(ExplosionComponent x)
        {
            Box b;
            return x != null && _made.TryGetValue(x, out b) ? b.side : NgMath.SideUnknown;
        }

        internal static int PushMaker(object maker)
        {
            if (_makerDepth >= _makers.Length)
                return -1;

            _makers[_makerDepth] = maker;
            return _makerDepth++;
        }

        internal static void PopMaker(int at)
        {
            if (at < 0)
                return;

            for (int i = at; i < _makerDepth; i++)
                _makers[i] = null;

            if (_makerDepth > at)
                _makerDepth = at;
        }

        internal static int PushHazard(Component h)
        {
            if (_hazardDepth >= _hazards.Length)
                return -1;

            _hazards[_hazardDepth] = h;
            return _hazardDepth++;
        }

        internal static void PopHazard(int at)
        {
            if (at < 0)
                return;

            for (int i = at; i < _hazardDepth; i++)
                _hazards[i] = null;

            if (_hazardDepth > at)
                _hazardDepth = at;
        }

        // ---- scaling ------------------------------------------------------------

        // An explosion hands EVERY victim the same list, so it is scaled into a
        // new one - scaling it in place would compound victim after victim.
        internal static IReadOnlyList<Damage> Scaled(IReadOnlyList<Damage> damages, float mult, out float before,
                                                     out float after)
        {
            before = after = 0f;

            if (damages == null)
                return null;

            var copy = new List<Damage>(damages.Count);

            foreach (Damage d in damages)
            {
                before += d.amount;
                copy.Add(new Damage(d.amount * mult, d.damageType));
                after += d.amount * mult;
            }

            return copy;
        }

        // ---- words --------------------------------------------------------------

        private static string NameIn(object source)
        {
            string n = Name(source);
            return n.Length > 0 ? " (" + n + ")" : "";
        }

        private static string NameOf(HealthBase victim)
        {
            string n = Name(victim);
            return n.Length > 0 ? n : "something";
        }

        private static string Name(object o)
        {
            try
            {
                UnityEngine.Object uo = o as UnityEngine.Object;

                if (uo != null)
                    return uo.name.Replace("(Clone)", "").Trim();

                WeaponBase w = o as WeaponBase;

                if (w != null && w.Owner != null)
                    return w.Owner.name.Replace("(Clone)", "").Trim() + "'s beam";

                CellType cell = o as CellType;

                if (cell != null)
                    return cell.name;
            }
            catch (Exception)
            {
            }

            return "";
        }

        private static string Num(float v)
        {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    // =========================================================================
    // THE PATCHES. Every one is gated on NgDamage.Active (off in a world with no
    // enemy damage - the first world, by default) and wrapped: a patch on the
    // damage path must never throw into the game.
    // =========================================================================

    // ---- the six entry points --------------------------------------------------

    [HarmonyPatch(typeof(HealthBase), "ProjectileCollided")]
    internal static class NgDamageShotPatch
    {
        static void Prefix(HealthBase __instance, IProjectile projectile, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.Enter(__instance, NgDamage.OfShot(projectile), NgDamage.RouteShot, projectile);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(HealthBase), "OnHitByHitscanWeapon")]
    internal static class NgDamageBeamPatch
    {
        static void Prefix(HealthBase __instance, HitscanWeapon weapon, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.Enter(__instance, NgDamage.OfBeam(weapon), NgDamage.RouteBeam, weapon);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(HealthBase), "OnExplosion")]
    internal static class NgDamageBlastPatch
    {
        static void Prefix(HealthBase __instance, Explosion explosion, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.Enter(__instance, NgDamage.OfBlast(explosion), NgDamage.RouteBlast,
                                         explosion.Owner);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(HealthBase), "OnHazardTouched")]
    internal static class NgDamageContactPatch
    {
        static void Prefix(HealthBase __instance, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.Enter(__instance, NgDamage.OfHazard(), NgDamage.RouteContact, null);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(HealthBase), "OnHitByElectricity")]
    internal static class NgDamageSparkPatch
    {
        static void Prefix(HealthBase __instance, ElectricityConductor conductor, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.Enter(__instance, NgDamage.OfSpark(conductor), NgDamage.RouteSpark, conductor);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    // Units bump terrain all the time; only a cell that does damage (the
    // hazard block, contact 1) opens a scope.
    [HarmonyPatch(typeof(HealthBase), "OnCellCollision")]
    internal static class NgDamageCellPatch
    {
        static void Prefix(HealthBase __instance, CellCollision cellCollision, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active || cellCollision.cellType == null ||
                cellCollision.cellType.contactDamage.amount <= 0f)
            {
                return;
            }

            try
            {
                __state = NgDamage.Enter(__instance, NgMath.SideWorld, NgDamage.RouteCell, cellCollision.cellType);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    // The slime cell (a DamageCellBehaviour, read only on the ships).
    [HarmonyPatch(typeof(DamageCellBehaviourTarget), "Update")]
    internal static class NgDamageSlimePatch
    {
        static void Prefix(DamageCellBehaviourTarget __instance, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.EnterSlime(__instance);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    // The burn tick.
    [HarmonyPatch(typeof(DamagableResource), "Update")]
    internal static class NgDamageBurnTickPatch
    {
        static void Prefix(DamagableResource __instance, out int __state)
        {
            __state = -1;

            if (!NgDamage.Active)
                return;

            try
            {
                __state = NgDamage.EnterBurnTick(__instance);
            }
            catch (Exception)
            {
                __state = -1;
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.Pop(__state);
            return __exception;
        }
    }

    // ---- where the multiply happens --------------------------------------------

    [HarmonyPatch(typeof(DamagableResource), "TakeDamage", new Type[] { typeof(Damage) })]
    internal static class NgDamageTakeOnePatch
    {
        static void Prefix(DamagableResource __instance, ref Damage damage)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                int at;
                float m = NgDamage.Take(__instance, out at);

                if (m == 1f)
                    return;

                float before = damage.amount;
                damage = new Damage(damage.amount * m, damage.damageType);
                NgDamage.Boost(at, __instance, before, damage.amount);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(DamagableResource), "TakeDamage", new Type[] { typeof(IReadOnlyList<Damage>) })]
    internal static class NgDamageTakeListPatch
    {
        static void Prefix(DamagableResource __instance, ref IReadOnlyList<Damage> damages)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                int at;
                float m = NgDamage.Take(__instance, out at);

                if (m == 1f)
                    return;

                float before, after;
                damages = NgDamage.Scaled(damages, m, out before, out after);
                NgDamage.Boost(at, __instance, before, after);
            }
            catch (Exception)
            {
            }
        }
    }

    // Plants, props: health with no unit.
    [HarmonyPatch(typeof(Health), "TakeDamage", new Type[] { typeof(Damage) })]
    internal static class NgDamageHealthOnePatch
    {
        static void Prefix(Health __instance, ref Damage damage)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                int at;
                float m = NgDamage.Take(__instance, out at);

                if (m == 1f)
                    return;

                float before = damage.amount;
                damage = new Damage(damage.amount * m, damage.damageType);
                NgDamage.Boost(at, __instance, before, damage.amount);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(Health), "TakeDamage", new Type[] { typeof(IReadOnlyList<Damage>) })]
    internal static class NgDamageHealthListPatch
    {
        static void Prefix(Health __instance, ref IReadOnlyList<Damage> damages)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                int at;
                float m = NgDamage.Take(__instance, out at);

                if (m == 1f)
                    return;

                float before, after;
                damages = NgDamage.Scaled(damages, m, out before, out after);
                NgDamage.Boost(at, __instance, before, after);
            }
            catch (Exception)
            {
            }
        }
    }

    // The burn tick's own call. After any TakeDamage the scope is already
    // used, so this scales only what reached Damage without one - the tick.
    [HarmonyPatch(typeof(DamagableResource), "Damage", new Type[] { typeof(float) })]
    internal static class NgDamageTickPatch
    {
        static void Prefix(DamagableResource __instance, ref float amount)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                int at;
                float m = NgDamage.Take(__instance, out at);

                if (m == 1f)
                    return;

                float before = amount;
                amount *= m;
                NgDamage.Boost(at, __instance, before, amount);
            }
            catch (Exception)
            {
            }
        }
    }

    // ---- who lit you --------------------------------------------------------------

    [HarmonyPatch(typeof(DamagableResource), "ApplyBurn")]
    internal static class NgDamageIgnitePatch
    {
        static void Prefix(DamagableResource __instance, MinMaxFloat burn)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                NgDamage.Ignited(__instance, burn);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(BurnCellBehaviourTarget), "Update")]
    internal static class NgDamageSteamPatch
    {
        static void Postfix(BurnCellBehaviourTarget __instance)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                NgDamage.InSteam(__instance);
            }
            catch (Exception)
            {
            }
        }
    }

    // ---- an enemy's shot is stamped as it is fired ---------------------------------

    [HarmonyPatch(typeof(Projectile), "Shoot")]
    internal static class NgDamageStampPatch
    {
        static void Postfix(Projectile __instance)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                NgDamage.OnShot(__instance);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(PhysicsProjectile), "Shoot")]
    internal static class NgDamageStampPhysicsPatch
    {
        static void Postfix(PhysicsProjectile __instance)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                NgDamage.OnShot(__instance);
            }
            catch (Exception)
            {
            }
        }
    }

    // ---- who is touching -----------------------------------------------------------

    [HarmonyPatch(typeof(Hazard), "OnCollisionEnter2D")]
    internal static class NgDamageHazardHitPatch
    {
        static void Prefix(Hazard __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushHazard(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopHazard(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Hazard), "OnTriggerEnter2D")]
    internal static class NgDamageHazardTouchPatch
    {
        static void Prefix(Hazard __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushHazard(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopHazard(__state);
            return __exception;
        }
    }

    // The beak ram (Larva, Child, Maggot, Swimmer Maggot): a swept cast that
    // hands its Hazard's damage on with no collision callback at all.
    [HarmonyPatch(typeof(ChargerHead), "OnObjectHit")]
    internal static class NgDamageRamPatch
    {
        static void Prefix(ChargerHead __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushHazard(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopHazard(__state);
            return __exception;
        }
    }

    // ---- who is making an explosion --------------------------------------------------

    [HarmonyPatch(typeof(DamagableResource), "Die")]
    internal static class NgDamageDiePatch
    {
        static void Prefix(DamagableResource __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Health), "Die")]
    internal static class NgDamageHealthDiePatch
    {
        static void Prefix(Health __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    // A shot's own effects (destroyEffect, spawnOnTimeDown) are Instantiated
    // inside these, and six of them are ownerless ExplosionComponents.
    [HarmonyPatch(typeof(Projectile), "HandleRange")]
    internal static class NgDamageShotRangePatch
    {
        static void Prefix(Projectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Projectile), "HandleLifetime")]
    internal static class NgDamageShotLifetimePatch
    {
        static void Prefix(Projectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Projectile), "OnObjectHit")]
    internal static class NgDamageShotHitPatch
    {
        static void Prefix(Projectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PhysicsProjectile), "HandleLifetime")]
    internal static class NgDamagePhysicsLifetimePatch
    {
        static void Prefix(PhysicsProjectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PhysicsProjectile), "Impact")]
    internal static class NgDamagePhysicsImpactPatch
    {
        static void Prefix(PhysicsProjectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushMaker(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopMaker(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ExplosionComponent), "Awake")]
    internal static class NgDamageBlastMadePatch
    {
        static void Postfix(ExplosionComponent __instance)
        {
            if (!NgDamage.Active)
                return;

            try
            {
                NgDamage.OnMade(__instance);
            }
            catch (Exception)
            {
            }
        }
    }

    // ---- who is setting one off ---------------------------------------------------------

    [HarmonyPatch(typeof(ExplosionComponent), "Start")]
    internal static class NgDamageBlastStartPatch
    {
        static void Prefix(ExplosionComponent __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushBlast(NgDamage.MadeSide(__instance)) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopBlast(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Projectile), "SpawnExplosion")]
    internal static class NgDamageShotBlastPatch
    {
        static void Prefix(Projectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushShotBlast(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopBlast(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PhysicsProjectile), "SpawnExplosion")]
    internal static class NgDamagePhysicsBlastPatch
    {
        static void Prefix(PhysicsProjectile __instance, out int __state)
        {
            __state = NgDamage.Active ? NgDamage.PushShotBlast(__instance) : -1;
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgDamage.PopBlast(__state);
            return __exception;
        }
    }
}
