using System;
using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // ★ ONE MARKER (DECIDED 3.6): the same opt-in bubble New Game Plus draws -
    // an amber ring with a plus in it, beside the enemy's upper right,
    // breathing - whichever DLL draws it. Both make a child named "NgMarker"
    // and skip an enemy that already has one, so an enemy marked by both is
    // marked once. A deliberate copy of NgMarker (the mods stand alone).
    //
    // Never a colour tint on the enemy: the game's hit flash owns its colour.
    internal sealed class EtMarker : MonoBehaviour
    {
        internal const string ChildName = "NgMarker";

        private static Sprite _sprite;
        private const int Size = 13;

        private Transform _target;
        private Vector3 _offset;
        private SpriteRenderer _sr;
        private float _phase;

        internal static void MaybeAttach(SavableEntity se)
        {
            if (se == null || EtSettings.EnemyMarker == null || !EtSettings.EnemyMarker.Value)
                return;

            try
            {
                if (se.transform.Find(ChildName) != null)
                    return;

                if (_sprite == null)
                    _sprite = MakeSprite();

                var go = new GameObject(ChildName);
                go.transform.SetParent(se.transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.color = new Color(1f, 0.62f, 0.15f, 1f);

                SpriteRenderer body = se.GetComponentInChildren<SpriteRenderer>();

                if (body != null && body != sr)
                {
                    sr.sortingLayerID = body.sortingLayerID;
                    sr.sortingOrder = body.sortingOrder + 50;
                }

                Vector2 ext = new Vector2(0.5f, 0.5f);
                Collider2D col = se.GetComponentInChildren<Collider2D>();

                if (col != null)
                    ext = col.bounds.extents;

                var m = go.AddComponent<EtMarker>();
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

            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 20f);
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            transform.position = _target.position + _offset;
            transform.rotation = Quaternion.identity;

            if (_sr != null)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f + _phase);
                _sr.color = new Color(1f, 0.62f, 0.15f, 0.7f + 0.3f * k);
                transform.localScale = Vector3.one * (0.92f + 0.12f * k);
            }
        }
    }

    // THE OPT-IN OVERLOAD GAUGE (DECIDED 3.2: none by default, "a setting to
    // show a very small one on the enemy"). One unit wide above the enemy:
    // blue as the meter fills, gold for the window. Never a HUD or log line.
    // The setting is read live, both ways.
    internal sealed class EtGauge : MonoBehaviour
    {
        private const float Width = 1f;
        private const float Height = 0.1f;

        private static Sprite _white;

        private EtUnit _unit;
        private Vector3 _offset;
        private SpriteRenderer _back, _fill;

        internal static EtGauge Attach(EtUnit unit)
        {
            GameObject made = null;

            try
            {
                if (_white == null)
                {
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
                }

                var go = new GameObject("EtGauge");
                made = go;
                go.transform.SetParent(unit.transform, false);

                var g = go.AddComponent<EtGauge>();
                g._unit = unit;

                Collider2D col = unit.GetComponentInChildren<Collider2D>();
                float top = col != null ? col.bounds.extents.y : 0.5f;
                g._offset = new Vector3(-Width * 0.5f, top + 0.25f, -0.01f);

                SpriteRenderer body = unit.GetComponentInChildren<SpriteRenderer>();
                g._back = g.Bar("back", new Color(0f, 0f, 0f, 0.6f), body, 60);
                g._fill = g.Bar("fill", new Color(0.35f, 0.75f, 1f, 0.95f), body, 61);
                g.LateUpdate();
                return g;
            }
            catch (Exception)
            {
                // Half-built is worse than none: a gauge with no bars would
                // throw every frame. Cosmetic only - it is simply not drawn.
                if (made != null)
                    UnityEngine.Object.Destroy(made);

                return null;
            }
        }

        private SpriteRenderer Bar(string name, Color colour, SpriteRenderer body, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _white;
            sr.color = colour;

            if (body != null)
            {
                sr.sortingLayerID = body.sortingLayerID;
                sr.sortingOrder = body.sortingOrder + order;
            }

            go.transform.localScale = new Vector3(Width, Height, 1f);
            return sr;
        }

        private void LateUpdate()
        {
            if (_unit == null || _back == null || _fill == null)
                return;

            bool on = EtSettings.OverloadGauge != null && EtSettings.OverloadGauge.Value;

            if (_back.enabled != on)
            {
                _back.enabled = on;
                _fill.enabled = on;
            }

            if (!on)
                return;

            transform.position = _unit.transform.position + _offset;
            transform.rotation = Quaternion.identity;

            float k;

            if (_unit.Overloaded)
            {
                k = Mathf.Max(_unit.OverloadFraction, 0.02f);
                _fill.color = new Color(1f, 0.85f, 0.25f, 0.95f);
            }
            else
            {
                k = _unit.Meter;
                _fill.color = new Color(0.35f, 0.75f, 1f, 0.95f);
            }

            _fill.transform.localScale = new Vector3(Width * Mathf.Clamp01(k), Height, 1f);
        }
    }
}
