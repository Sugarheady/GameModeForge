using System;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // THE SHOT TRAITS: grow and ricochet (CONCEPT.md 3.6, 3.7).
    //
    // ★ STAMPED ON THE SHOT WHEN IT IS FIRED, AND THE SHOT'S FROM THEN ON.
    // DECIDED 3.4: a bullet Weapon Forge's `deflect` / `rally` knocks back
    // keeps swelling and bouncing once it is yours. So a shot trait is a
    // component on the live shot, never looked up through its owner again.
    // Shots are never pooled (every one is Instantiated and Destroyed,
    // measured), so a stamped shot needs no clean-up.
    //
    // ★ A BULLET'S BOUNCE SETTINGS ARE SHARED. `ProjectileBounceData` is a
    // CLASS handed by reference from the WeaponData to the gun to every bullet,
    // so the ricochet gives each bullet its OWN instance - writing `enabled`
    // on the one it came with would make every bullet of that kind bounce, on
    // every enemy. A rocket's impact switch is a struct, so it is already the
    // shot's own copy.
    //
    // ★ ONLY TERRAIN IS A BOUNCE. Touching your ship, drones, minions or
    // wingmen ends the shot as the game would. The two bullets that already
    // bounce (`CrossRedElectron Bouncer`, `SoldierTech`) and the four drifting
    // mines that roll on a fuse (`Ammo FlyAlfa`: the Queen's bomb, both bomber
    // bombs, the Fly Alfa) keep their own behaviour.
    internal static class EtShots
    {
        // Counted per world, for WORLD INFO.
        internal static int Grown, Bounced;

        // Live stamped shots - the hot-path patch (every bullet hit in the
        // game) returns at once while there are none.
        internal static int LiveRicochet;

        internal static void Reset()
        {
            Grown = 0;
            Bounced = 0;
        }

        // A stamped shot is done bouncing. Never below 0: a count that drifted
        // negative would switch the hot-path gate off for good.
        internal static void Release()
        {
            if (LiveRicochet > 0)
                LiveRicochet--;
        }

        private static int _ground = -2;

        internal static int Ground
        {
            get
            {
                if (_ground == -2)
                    _ground = LayerMask.NameToLayer("Ground");

                return _ground;
            }
        }

        // ---- stamping ----------------------------------------------------------------

        // Projectile.Shoot, postfix.
        internal static void OnShoot(Projectile p)
        {
            if (p == null || p.Owner == null || p.GetComponent<EtShot>() != null)
                return;

            EtUnit u = EtUnit.Of(p.Owner);

            if (u == null || u.now == null)
                return;

            bool grow = u.Has(EtMath.Grow);
            bool ric = u.Has(EtMath.Ricochet) && u.now.bounces > 0 && Ground >= 0;

            // Already a bouncer: it keeps its own (unlimited) bounce.
            if (ric && p.ProjectileBounceData != null && p.ProjectileBounceData.enabled)
                ric = false;

            if (!grow && !ric)
                return;

            p.gameObject.AddComponent<EtShot>().Begin(p, null, u.now, grow, ric);
        }

        // PhysicsProjectile.Shoot, postfix.
        internal static void OnShootPhysics(PhysicsProjectile p)
        {
            if (p == null)
                return;

            // ★ ProjectileWeapon.FireSingle calls Shoot() TWICE on a physics
            // shot, the first time on a BLANK (no owner, no velocity yet).
            if (p.Owner == null && p.Velocity.sqrMagnitude <= 0.000001f)
                return;

            if (p.Owner == null || p.GetComponent<EtShot>() != null)
                return;

            EtUnit u = EtUnit.Of(p.Owner);

            if (u == null || u.now == null)
                return;

            bool grow = u.Has(EtMath.Grow) && u.now.growRockets;

            // A shot whose impact is already off rolls on a fuse and bounces
            // as it is (the drifting mines): it keeps its own behaviour.
            bool ric = u.Has(EtMath.Ricochet) && u.now.ricochetRockets && u.now.bounces > 0 &&
                       p.ImpactBehaviour.enabled && Ground >= 0;

            if (!grow && !ric)
                return;

            p.gameObject.AddComponent<EtShot>().Begin(null, p, u.now, grow, ric);
        }

        // ---- a bullet meets something (Projectile.OnObjectHit) -----------------------

        internal static void BeforeHit(Projectile p, RaycastHit2D hit)
        {
            if (LiveRicochet <= 0 || p == null || hit.collider == null)
                return;

            EtShot s = p.GetComponent<EtShot>();

            if (s == null || !s.ricochet)
                return;

            // Not terrain: the game's own path ends it (on you, it hits you).
            if (hit.collider.gameObject.layer != Ground)
                return;

            if (s.remaining > 0)
            {
                // Our bounce data (on, terrain only) makes the game reflect it.
                s.remaining--;
                s.bouncing = true;
                s.normal = hit.normal;
                return;
            }

            // Out of bounces: the next wall ends it, as for any bullet.
            p.ProjectileBounceData = new ProjectileBounceData { enabled = false, layerMask = 0 };
            s.ricochet = false;
            Release();
        }

        internal static void AfterHit(Projectile p)
        {
            if (LiveRicochet <= 0 || p == null)
                return;

            EtShot s = p.GetComponent<EtShot>();

            if (s == null || !s.bouncing)
                return;

            s.bouncing = false;
            Bounced++;

            if (!s.towardYou)
                return;

            Transform target = Target(p.Owner, p.transform.position);

            if (target == null)
                return;

            Vector2 dir = ((Vector2)target.position - (Vector2)p.transform.position).normalized;

            if (dir.sqrMagnitude < 0.5f)
                return;

            // ★ Only off the open side of the wall. With you behind it, aiming
            // at you is aiming back into the rock: the next step would hit the
            // same wall and spend a bounce, every step, until none were left.
            // It keeps the plain mirror instead.
            if (Vector2.Dot(dir, s.normal) <= 0.05f)
                return;

            // Direction through the game's own base heading too, or the next
            // wobble rebuilds Velocity from the old one.
            p.Velocity = dir * p.Velocity.magnitude;
            EtShotReflect.SetIdeal(p, dir);
        }

        // ★ "TOWARD YOU" MEANS THE SHOT'S ENEMIES (3.6: Weapon Forge's `seek`,
        // which on an enemy shot aims at you): the nearest live ship the shot's
        // owner is an enemy of. A shot that is yours now (knocked back) or
        // whose shooter is on your side (charmed) has no one to seek.
        internal static Transform Target(Unit owner, Vector2 from)
        {
            if (owner == null || owner.GetComponentInParent<Ship>() != null)
                return null;

            ShipManager ships;

            if (!ServiceLocator.TryGet<ShipManager>(out ships) || ships == null)
                return null;

            Transform best = null;
            float bestD = float.MaxValue;

            foreach (Ship ship in ships.Ships)
            {
                if (ship == null || ship.IsDead || ship.Unit == null || !owner.IsEnemiesWith(ship.Unit))
                    continue;

                float d = ((Vector2)ship.transform.position - from).sqrMagnitude;

                if (d < bestD)
                {
                    bestD = d;
                    best = ship.transform;
                }
            }

            return best;
        }
    }

    // One stamped shot.
    internal sealed class EtShot : MonoBehaviour
    {
        private Projectile _bullet;
        private PhysicsProjectile _rocket;

        // grow
        private bool _grow;
        private float _from, _to, _curve, _span, _startTime, _baseRadius;
        private bool _overTime, _hitbox, _clamp;
        private Vector2 _origin;
        private Vector3 _baseScale;

        // ricochet
        internal bool ricochet;
        internal int remaining;
        internal bool towardYou;
        internal bool bouncing;
        internal Vector2 normal;
        private float _bounciness;
        private ProjectileImpactBehaviour _impact;

        internal void Begin(Projectile bullet, PhysicsProjectile rocket, EtNow now, bool grow, bool ric)
        {
            _bullet = bullet;
            _rocket = rocket;

            if (grow)
            {
                _grow = true;
                _from = now.from;
                _to = now.to;
                _curve = now.curve;
                _overTime = now.overTime;
                _hitbox = now.hitbox;
                _clamp = now.clamp;
                _startTime = Time.time;
                _origin = transform.position;
                _baseScale = transform.localScale;
                _baseRadius = bullet != null ? bullet.Radius : 0f;

                float range = 0f, life = 0f;

                if (bullet != null)
                {
                    range = bullet.RangeData.enabled ? bullet.RangeData.range : 0f;
                    life = bullet.LifetimeData.enabled ? bullet.LifetimeData.time : 0f;
                }
                else if (rocket != null)
                {
                    // `rangeData` is inert on a physics shot: its lifetime is its bound.
                    life = rocket.LifetimeData.enabled ? rocket.LifetimeData.time : 0f;
                }

                // ★ A physics shot (rocket, lobbed shot) has no range the game
                // enforces - its LIFETIME is its bound - and it does not fly at
                // its launch speed (a rocket settles on its engine's cruise, a
                // grenade falls), so "over its range" is over its life, by time.
                if (rocket != null && now.span <= 0f && !_overTime && life > 0f)
                    _overTime = true;

                _span = now.span > 0f ? now.span
                      : (!_overTime && range > 0f) ? range
                      : (_overTime && life > 0f) ? life
                      : (_overTime ? 2f : 10f);

                EtShots.Grown++;
                Apply();
            }

            if (ric)
            {
                ricochet = true;
                remaining = now.bounces;
                towardYou = now.towardYou;
                _bounciness = now.bounciness;
                EtShots.LiveRicochet++;

                if (bullet != null)
                {
                    // ★ Its OWN instance (the one it came with is shared).
                    bullet.ProjectileBounceData = new ProjectileBounceData
                    {
                        enabled = true,
                        layerMask = 1 << EtShots.Ground
                    };
                }
                else if (rocket != null)
                {
                    // Impact off, so the physics bounces it; kept, so the end
                    // of the shot is the game's own impact.
                    _impact = rocket.ImpactBehaviour;
                    ProjectileImpactBehaviour off = _impact;
                    off.enabled = false;
                    rocket.ImpactBehaviour = off;
                }
            }
        }

        private void OnDestroy()
        {
            if (ricochet)
            {
                ricochet = false;
                EtShots.Release();
            }
        }

        // ---- grow ------------------------------------------------------------------

        private void FixedUpdate()
        {
            if (!_grow)
                return;

            try
            {
                Apply();
            }
            catch (Exception)
            {
                _grow = false;
            }
        }

        private void Apply()
        {
            float span = _span > 0f ? _span : 1f;
            float t = _overTime
                ? (Time.time - _startTime) / span
                : Vector2.Distance(_origin, transform.position) / span;

            t = _clamp ? Mathf.Clamp01(t) : Mathf.Max(0f, t);

            float eased = Mathf.Approximately(_curve, 1f) ? t : Mathf.Pow(t, _curve);
            float scale = Mathf.LerpUnclamped(_from, _to, eased);

            transform.localScale = _baseScale * scale;

            // Radius is public and the game rebuilds its sweep from it every
            // step: the real hitbox grows. A physics shot's collider scales
            // with its transform, so its hitbox always follows.
            if (_hitbox && _bullet != null)
                _bullet.Radius = _baseRadius * scale;
        }

        // ---- a rocket or lobbed shot meets something --------------------------------
        //
        // ★ THROUGH A PREFIX AND A POSTFIX ON THE GAME'S OWN
        // `PhysicsProjectile.OnCollisionEnter2D` (EtRocketHitPatch), never a
        // collision message of our own. Unity does not promise which of two
        // components on one object hears a collision first, and the game's
        // handler reads the impact switch: had it heard the contact AFTER we
        // switched the impact back on and ran it ourselves, the rocket would
        // have gone off twice. The prefix runs before the game's body, always.

        // Prefix: is this contact a BOUNCE (true: the postfix does it)? If it
        // is the END - not terrain (you, your drones, a plant), or no bounces
        // left - the shot's own impact goes back on HERE, so the game's own
        // handler, running next, ends it exactly as it always would.
        internal bool BeforeContact(Collision2D c)
        {
            if (_rocket == null || !ricochet || c == null || c.contactCount <= 0)
                return false;

            Collider2D other = c.collider;
            bool terrain = other != null && other.gameObject.layer == EtShots.Ground;

            if (terrain && remaining > 0)
                return true;

            End();
            return false;
        }

        // Postfix, after the game pushed (impact off: nothing else).
        internal void Bounce(Collision2D c)
        {
            if (_rocket == null || !ricochet || c == null || c.contactCount <= 0)
                return;

            ContactPoint2D contact = c.GetContact(0);
            Rigidbody2D rb = c.otherRigidbody != null ? c.otherRigidbody : GetComponent<Rigidbody2D>();

            if (rb == null)
                return;

            ProjectileHomingData h = _rocket.HomingData;
            bool engine = h.maxSpeed > 0f && h.acceleration > 0f;

            Vector2 n = contact.normal;
            float closing = Vector2.Dot(c.relativeVelocity, n);
            Vector2 v = rb.linearVelocity;

            float ox, oy;
            bool bounced;

            // A rocket keeps its speed (a mirror); a lobbed shot comes off at
            // `bounciness`, and a contact too gentle to be a bounce (a shot
            // settling on the floor) is not counted.
            EtMath.Rebound(v.x, v.y, n.x, n.y, Mathf.Max(0f, closing), engine ? 1f : _bounciness,
                           engine ? 0f : 1f, out ox, out oy, out bounced);

            if (!bounced)
                return;

            remaining--;
            EtShots.Bounced++;

            var outgoing = new Vector2(ox, oy);
            rb.linearVelocity = outgoing;

            if (!engine || outgoing.sqrMagnitude < 0.0001f)
                return;

            // A rocket steers at its target point every step: point it - and
            // its nose - along the rebound, or it turns straight back into the
            // wall.
            rb.rotation = Mathf.Atan2(outgoing.y, outgoing.x) * Mathf.Rad2Deg;
            rb.angularVelocity = 0f;
            Relock(outgoing.normalized);
        }

        // ★ DECIDED 3.7: A BOUNCE DROPS THE LOCK. The rocket flies off along
        // the rebound; with "toward you" on, it locks back onto you.
        private void Relock(Vector2 dir)
        {
            if (towardYou)
            {
                Transform t = EtShots.Target(_rocket.Owner, transform.position);

                if (t != null)
                {
                    _rocket.Target = t.gameObject;
                    ProjectileHomingData on = _rocket.HomingData;
                    on.enabled = true;
                    _rocket.HomingData = on;
                    return;
                }
            }

            ProjectileHomingData h = _rocket.HomingData;
            h.enabled = false;          // nothing re-acquires a target
            _rocket.HomingData = h;
            _rocket.Target = null;
            EtShotReflect.SetTargetPosition(_rocket, (Vector2)transform.position + dir * 10000f);
        }

        // Done bouncing: the shot's own impact back on, so the game's own code -
        // this contact's handler, its shield and slime triggers - ends it.
        internal void End()
        {
            if (ricochet)
            {
                ricochet = false;
                EtShots.Release();
            }

            if (_rocket != null)
                _rocket.ImpactBehaviour = _impact;
        }
    }

    // The private members the shot traits reach, made safely.
    internal static class EtShotReflect
    {
        private static readonly AccessTools.FieldRef<Projectile, Vector2> _ideal = Make<Projectile, Vector2>("idealDirection");
        private static readonly AccessTools.FieldRef<PhysicsProjectile, Vector2> _target =
            Make<PhysicsProjectile, Vector2>("targetPosition");

        private static AccessTools.FieldRef<T, F> Make<T, F>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void SetIdeal(Projectile p, Vector2 dir)
        {
            if (_ideal != null)
                _ideal(p) = dir;
        }

        internal static void SetTargetPosition(PhysicsProjectile p, Vector2 at)
        {
            if (_target != null)
                _target(p) = at;
        }
    }

    // ---- the patches ------------------------------------------------------------

    [HarmonyPatch(typeof(Projectile), "Shoot")]
    internal static class EtShootPatch
    {
        static void Postfix(Projectile __instance)
        {
            try
            {
                EtShots.OnShoot(__instance);
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("a shot trait could not start: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(PhysicsProjectile), "Shoot")]
    internal static class EtShootPhysicsPatch
    {
        static void Postfix(PhysicsProjectile __instance)
        {
            try
            {
                EtShots.OnShootPhysics(__instance);
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("a shot trait could not start on a rocket: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "OnObjectHit")]
    internal static class EtHitPatch
    {
        static void Prefix(Projectile __instance, RaycastHit2D hit)
        {
            try
            {
                EtShots.BeforeHit(__instance, hit);
            }
            catch (Exception)
            {
                // Every bullet hit in the game: it must never throw.
            }
        }

        static void Postfix(Projectile __instance)
        {
            try
            {
                EtShots.AfterHit(__instance);
            }
            catch (Exception)
            {
            }
        }
    }

    // A rocket or lobbed shot meets something: the prefix decides bounce or end
    // BEFORE the game's handler reads the impact switch; the postfix does the
    // bounce after it. A throw here must never cost the shot its ending, so
    // the catch puts the shot's own impact back.
    [HarmonyPatch(typeof(PhysicsProjectile), "OnCollisionEnter2D")]
    internal static class EtRocketHitPatch
    {
        static void Prefix(PhysicsProjectile __instance, Collision2D collision, out bool __state)
        {
            __state = false;

            if (EtShots.LiveRicochet <= 0 || __instance == null)
                return;

            EtShot s = __instance.GetComponent<EtShot>();

            if (s == null || !s.ricochet)
                return;

            try
            {
                __state = s.BeforeContact(collision);
            }
            catch (Exception ex)
            {
                s.End();
                EnemyTraitsPlugin.Log.LogError("a ricochet failed - the shot ends as the game would: " + ex);
            }
        }

        static void Postfix(PhysicsProjectile __instance, Collision2D collision, bool __state)
        {
            if (!__state || __instance == null)
                return;

            EtShot s = __instance.GetComponent<EtShot>();

            if (s == null)
                return;

            try
            {
                s.Bounce(collision);
            }
            catch (Exception ex)
            {
                s.End();
                EnemyTraitsPlugin.Log.LogError("a ricochet failed - the shot ends at its next contact: " + ex);
            }
        }
    }

    // The overload's "shots" fill: a shot that really fired (the real Shoot
    // stamps the fire-rate clock, so FireRatePassed reads false after it; a
    // blocked press does not). Volleys press Shoot directly, so they count.
    [HarmonyPatch(typeof(Shooter), "Shoot")]
    internal static class EtShotCountPatch
    {
        static void Postfix(Shooter __instance)
        {
            EtNow n = EtRun.Cached;

            if (n == null || n.fill != "shots" || n.coverage[EtMath.Overload] <= 0f || __instance == null)
                return;

            try
            {
                if (__instance.FireRatePassed)
                    return;

                EtUnit u = EtUnit.Of(__instance.Unit);

                if (u != null)
                    u.NoteShot();
            }
            catch (Exception)
            {
            }
        }
    }

    // The overload's "hits" fill: an enemy's shot or beam striking one of
    // your ships.
    [HarmonyPatch(typeof(DamagableResource), "ProjectileCollided")]
    internal static class EtProjectileHitPatch
    {
        static void Postfix(DamagableResource __instance, IProjectile projectile)
        {
            if (!EtHits.Wanted || projectile == null)
                return;

            try
            {
                EtHits.Note(__instance, projectile.Owner);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(DamagableResource), "OnHitByHitscanWeapon")]
    internal static class EtBeamHitPatch
    {
        static void Postfix(DamagableResource __instance, HitscanWeapon weapon)
        {
            if (!EtHits.Wanted || weapon == null)
                return;

            try
            {
                EtHits.Note(__instance, weapon.Owner);
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class EtHits
    {
        internal static bool Wanted
        {
            get
            {
                EtNow n = EtRun.Cached;
                return n != null && n.fill == "hits" && n.coverage[EtMath.Overload] > 0f;
            }
        }

        internal static void Note(Component victim, Unit attacker)
        {
            if (victim == null || attacker == null || victim.GetComponentInParent<Ship>() == null)
                return;

            EtUnit u = EtUnit.Of(attacker);

            if (u != null)
                u.NoteHit();
        }
    }
}
