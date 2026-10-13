using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

namespace GameModeForge.NewGamePlus
{
    // HEALTH BARS IN LAYERS - stage 2's first piece (built 2026-10-05).
    //
    // R21 300, his note: "every new game plus increase that healthbar size and
    // covers the players screen ... Maybe do the Streetfighter changing color
    // healthbar". DECIDED 2026-10-05 (CONCEPT.md section 7, answer 7):
    //   - layers for the Queens AND every other enemy, one look everywhere;
    //   - a layer is the enemy's NORMAL health, so a bar never grows;
    //   - the partial layer is the top one; the last layer is the bar the game
    //     always drew, in the enemy's own colour (his pick, "Own colour last");
    //   - the layers above it count up green, yellow, orange, red, purple, then
    //     round again, and an "xN" beside the bar counts the layers left.
    //   - his ask the same day: the colours are a SETTING - a tab row steps
    //     through palettes (CLASSIC is the one above), the last of them CUSTOM,
    //     his own list in the cfg. The last layer stays the enemy's own colour
    //     whatever the palette ("Keep own colour last").
    // The arithmetic is NgMath's (LayerCount / LayerIndex / InLayer /
    // LayerSlot / the palettes and the colour parsing), compiled and run by
    // ngmathprobe.py.
    //
    // ---- HOW: A STAND-IN TANK, AND THE GAME DRAWS IT ------------------------
    //
    // Both bars draw whatever ResourceTank they are given: one segment per
    // point (the enemy's `ResourceBar`, rows of `HealthbarOwner.maxUnitsPerRow`)
    // or a row of 7.5-pixel units per point (the Queens' `BossHealthbar`, rows
    // of 200). So neither bar is redrawn here. A buffed enemy's bar is handed a
    // STAND-IN tank whose capacity is one layer and whose value is the health
    // inside the current layer, kept in step with the real tank just before
    // the bar reads it (a prefix on each bar's Update). The real tank is never
    // written. ResourceTank is a plain class with a public setter for both.
    //
    // ★ WRAPPED WHEN THE BAR IS MADE, LAYERED WHEN THE BUFF LANDS. An enemy is
    // buffed two frames after it appears (NgBuffs - the owner trap), and a
    // restored enemy that is already hurt gets its bar at once. So every
    // enemy's health bar is wrapped while health is buffed this world, and the
    // stand-in simply mirrors the real tank until NgBuffs says this enemy is
    // buffed - then it splits into layers.
    //
    // ★ THE EMPTY HALF OF A SEGMENT IS FAINT. Measured: each resource's bar
    // texture has two halves - solid (full) below, a faint outline or a 20%
    // block (empty) above - and the game's full and empty COLOURS are the same.
    // A layer's "rest of the bar" must read as the next layer, solid, so a
    // layered row is given a copy of its texture whose top half is the solid
    // bottom half (a GPU copy - the game's textures are not readable), and the
    // shader's empty colour is the next layer's. The last layer gets the
    // game's own texture and colours back.
    //
    // ★ THE SEGMENTS THAT JUST CHANGED ARE DRAWN BY OTHER OBJECTS. For 1.5 s
    // after a segment fills or empties, a `ResourceUnit` draws it (the white
    // hit flash, then the empty sprite fading in) and the shader masks that
    // stretch. Those units take the resource's colours in `Setup`, so each one
    // is repainted as it is made (`ResourceBarRow.CreateUnit`). Their clips
    // animate only alpha and position, so the paint holds.
    //
    // ★ THE QUEEN'S BAR IS A FILL OVER A GREY BACKGROUND (`Empty`), with a
    // white "recent damage" strip between them. A layer above the last turns
    // the grey into the next layer's colour. When a layer breaks, the white
    // strip restarts inside the new layer (`delta` = the new value), or it
    // would sit far below the bar and show nothing for a long time.
    //
    // Never yours: anything a ship owns is left alone, like the buff itself.
    internal static class NgLayers
    {
        private const string LabelName = "NgLayerCount";

        // ---- the palette (his ask: the colours are a setting) ---------------
        //
        // `[Display] HealthBarPalette` (a tab row steps through them) and, for
        // Custom, `HealthBarLayerColours` - both read live. Resolved only when
        // either changes; every bar repaints when the version moves.
        private static float[][] _palette;
        private static NgLayerPalette _paletteChosen = (NgLayerPalette)(-1);
        private static string _paletteCustom;
        private static int _paletteVersion;
        private static readonly HashSet<string> _saidBadColour = new HashSet<string>();

        private static float[][] Palette()
        {
            NgLayerPalette chosen = NgSettings.HealthBarPalette != null
                ? NgSettings.HealthBarPalette.Value : NgLayerPalette.Classic;
            string custom = NgSettings.HealthBarLayerColours != null
                ? NgSettings.HealthBarLayerColours.Value : NgMath.DefaultCustomColours;

            if (_palette != null && chosen == _paletteChosen &&
                (chosen != NgLayerPalette.Custom || ReferenceEquals(custom, _paletteCustom) || custom == _paletteCustom))
            {
                return _palette;
            }

            float[][] resolved;

            if (chosen == NgLayerPalette.Custom)
            {
                var bad = new List<string>();
                resolved = NgMath.ParseColourList(custom, bad);

                foreach (string b in bad)
                {
                    if (_saidBadColour.Add(b))
                    {
                        NewGamePlusPlugin.Log.LogWarning(
                            "health bars in layers: '" + b + "' in HealthBarLayerColours is not a colour " +
                            "New Game Plus knows - it is skipped. Use a name (green, sky blue, crimson...) " +
                            "or a hex colour like #FF8800.");
                    }
                }

                if (resolved.Length == 0)
                {
                    resolved = NgMath.PaletteColours((int)NgLayerPalette.Classic);

                    if (_saidBadColour.Add("(none)"))
                    {
                        NewGamePlusPlugin.Log.LogWarning(
                            "health bars in layers: HealthBarLayerColours has no colour New Game Plus " +
                            "can read, so the Custom palette uses Classic's.");
                    }
                }
            }
            else
            {
                resolved = NgMath.PaletteColours((int)chosen);
            }

            _palette = resolved;
            _paletteChosen = chosen;
            _paletteCustom = custom;
            _paletteVersion++;

            NewGamePlusPlugin.Log.LogInfo(
                "health bars in layers: the layer colours are " + NgMath.PaletteNames[Math.Max(0,
                Math.Min((int)chosen, NgMath.PaletteNames.Length - 1))] + " (" + resolved.Length +
                " colour(s)); the last layer keeps each enemy's own colour.");

            return _palette;
        }

        // ★ DECLARED ABOVE THE FIELD REFS (the NgDamage lesson): static
        // initializers run in written order, and Ref() appends to this.
        private static string _missingFields = "";

        private static readonly AccessTools.FieldRef<HealthbarWidget, HealthbarOwner> WidgetOwner =
            Ref<HealthbarWidget, HealthbarOwner>("healthbarOwner");
        private static readonly AccessTools.FieldRef<ResourceBar, List<ResourceBarRow>> BarRows =
            Ref<ResourceBar, List<ResourceBarRow>>("rows");
        private static readonly AccessTools.FieldRef<ResourceBarRow, Material> RowMaterial =
            Ref<ResourceBarRow, Material>("material");
        private static readonly AccessTools.FieldRef<ResourceBarRow, RawImage> RowImage =
            Ref<ResourceBarRow, RawImage>("image");
        private static readonly AccessTools.FieldRef<BossHealthbar, List<BossHealthbarRow>> BossRows =
            Ref<BossHealthbar, List<BossHealthbarRow>>("rows");
        private static readonly AccessTools.FieldRef<BossHealthbar, float> BossDelta =
            Ref<BossHealthbar, float>("delta");
        private static readonly AccessTools.FieldRef<BossHealthbarRow, Image> BossFill =
            Ref<BossHealthbarRow, Image>("fillImage");

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

        // One enemy's bar and its stand-in tank.
        private sealed class View
        {
            internal ResourceTank Real;
            internal ResourceTank Shadow;
            internal Resource Resource;
            internal int Id;
            internal string Kind;
            internal int Index = -1;            // the layer painted last (-1: not yet)
            internal int Painted = -1;          // the palette version it was painted in
            internal Component Label;           // the "xN"
            internal float LabelBase;           // its size at 100% (NgMath.*CountBase)
            internal int LabelSized = -1;       // the HealthBarCountSize it was sized for
            internal Color EmptyStock;        // the Queen's rows: the background's own grey
            internal bool HaveEmptyStock;
        }

        // Weak, so a bar destroyed with its enemy takes its entry with it.
        private static ConditionalWeakTable<ResourceBar, View> _bars =
            new ConditionalWeakTable<ResourceBar, View>();

        private static bool _anyBars;
        private static BossHealthbar _bossBar;
        private static View _boss;

        // Set while HealthbarWidget.GenerateResourceBars runs: whose bars these are.
        internal static HealthbarOwner Generating;

        internal static HealthbarOwner OwnerOf(HealthbarWidget widget)
        {
            return WidgetOwner != null && widget != null ? WidgetOwner(widget) : null;
        }

        // The game's segment textures -> a copy with both halves solid.
        private static readonly Dictionary<Texture, Texture2D> _solid = new Dictionary<Texture, Texture2D>();
        private static readonly Dictionary<Texture, Texture> _stockOf = new Dictionary<Texture, Texture>();
        private static bool _saidNoCopy;

        // A segment unit's own empty sprite, kept while it wears the solid one.
        private static readonly ConditionalWeakTable<ResourceUnit, Sprite> _unitEmpty =
            new ConditionalWeakTable<ResourceUnit, Sprite>();

        private static bool _saidBar, _saidBoss, _saidMissing;
        private static object _font, _fontMaterial;
        private static bool _lookedForFont;

        internal static void Reset()
        {
            _bars = new ConditionalWeakTable<ResourceBar, View>();
            _anyBars = false;
            _bossBar = null;
            _boss = null;
            Generating = null;
            _saidBar = _saidBoss = false;
            // The palette and its said-once list are not a run's: kept.
        }

        // The switch (NgSettings.HealthBarLayers, default ON), read live.
        internal static bool On
        {
            get { return NgSettings.HealthBarLayers == null || NgSettings.HealthBarLayers.Value; }
        }

        // ---- the layer arithmetic for one bar -------------------------------

        // One layer, or 0 for "not layered": this enemy is buffed this world
        // and the switch is on. The buff multiplied the enemy's health by the
        // world's multiplier, so one layer is the capacity divided by it.
        private static float LayerOf(View v)
        {
            if (!On || v.Real == null || !NgBuffs.IsBuffed(v.Id))
                return 0f;

            float mult = NgMath.Multiplier(NgRun.HealthPercent);

            return mult > 1.0001f ? v.Real.Capacity / mult : 0f;
        }

        private static void Compute(View v, out int index, out float capacity, out float value)
        {
            capacity = v.Real.Capacity;
            value = v.Real.Value;
            index = 0;

            float layer = LayerOf(v);

            if (layer <= 0f || NgMath.LayerCount(capacity, layer) <= 1)
                return;

            index = NgMath.LayerIndex(value, capacity, layer);
            value = NgMath.InLayer(value, index, layer);
            capacity = layer;
        }

        private static Color ColourOf(int index, Resource own)
        {
            Color mine = own.resourceBarUnitColorFull;
            float[][] palette = Palette();
            int slot = NgMath.LayerSlot(index, palette, new[] { mine.r, mine.g, mine.b });

            if (slot < 0)
                return mine;

            float[] c = palette[slot];
            return new Color(c[0], c[1], c[2], 1f);
        }

        private static ResourceTank NewShadow(View v)
        {
            int index;
            float capacity, value;

            Compute(v, out index, out capacity, out value);

            var shadow = new ResourceTank { resource = v.Real.resource };
            shadow.Capacity = capacity;
            shadow.Value = value;
            shadow.IsLow = v.Real.IsLow;
            return shadow;
        }

        // ---- an ordinary enemy's bar ------------------------------------------

        // ResourceBar.Assign, prefix (from HealthbarWidget.GenerateResourceBars):
        // the bar for the health this enemy takes damage through gets the
        // stand-in. Shields and everything else keep the real tank.
        internal static ResourceTank WrapBar(ResourceBar bar, ResourceTank tank)
        {
            HealthbarOwner owner = Generating;

            // Wrapped whatever the switch says: a stand-in that is not layered
            // is the real tank exactly, so the switch can be flipped mid-fight
            // and every bar follows at once, both ways.
            if (owner == null || bar == null || tank == null || tank.resource == null ||
                owner.ShowAsBoss || NgRun.HealthPercent <= 0f)
            {
                return tank;
            }

            Unit unit = owner.Unit;

            if (unit == null)
                return tank;

            DamagableResource health = unit.GetComponent<DamagableResource>();
            Unit.Data data = unit.ComponentData;

            if (health == null || health.resource != tank.resource ||
                data == null || data.entity == null || NgBuffs.OwnedByAShip(data))
            {
                return tank;
            }

            SayMissing();

            var v = new View
            {
                Real = tank,
                Resource = tank.resource,
                Id = data.entity.instanceId,
                Kind = data.entity.entityId,
            };
            v.Shadow = NewShadow(v);

            _bars.Remove(bar);
            _bars.Add(bar, v);
            _anyBars = true;

            return v.Shadow;
        }

        // ResourceBar.Update, prefix: bring the stand-in up to date before the
        // bar draws it.
        internal static void SyncBar(ResourceBar bar)
        {
            View v;

            if (!_anyBars || bar == null || !_bars.TryGetValue(bar, out v) || v.Real == null)
                return;

            int index;
            float capacity, value;

            Compute(v, out index, out capacity, out value);

            bool rebuilt = false;

            if (Math.Abs(v.Shadow.Capacity - capacity) > 0.0001f)
            {
                v.Shadow.Capacity = capacity;
                v.Shadow.Value = value;
                bar.CheckCapacityChanged();     // new rows come out in the game's colours
                rebuilt = true;
            }

            v.Shadow.Value = value;
            v.Shadow.IsLow = v.Real.IsLow;

            // A size changed in the tab resizes the xN at once (and makes one a
            // hidden size never made).
            if (v.LabelSized != CountSize())
            {
                if (v.Label != null)
                    Resize(v, index);
                else if (index > 0)
                    LabelBar(bar, v, index);

                v.LabelSized = CountSize();
            }

            // A palette changed in the tab repaints a layered bar at once.
            if (index > 0)
                Palette();

            if (rebuilt || index != v.Index || (index > 0 && v.Painted != _paletteVersion))
            {
                PaintBar(bar, v, index);
                v.Painted = _paletteVersion;

                if (index > 0 && !_saidBar)
                {
                    _saidBar = true;
                    NewGamePlusPlugin.Log.LogInfo(
                        "health bars in layers: " + v.Kind + "'s bar shows " + (index + 1) +
                        " layers of " + Math.Round(capacity, 1) + " health (" + NgMath.LayerCountText(index) +
                        ") instead of one bar of " + Math.Round(v.Real.Capacity, 1) + ". Said once a world.");
                }

                v.Index = index;
            }
        }

        private static void PaintBar(ResourceBar bar, View v, int index)
        {
            Color full = ColourOf(index, v.Resource);
            Color empty = index > 0 ? ColourOf(index - 1, v.Resource) : v.Resource.resourceBarUnitColorEmpty;

            List<ResourceBarRow> rows = BarRows != null ? BarRows(bar) : null;

            if (rows != null)
            {
                foreach (ResourceBarRow row in rows)
                {
                    if (row == null)
                        continue;

                    Material m = RowMaterial != null ? RowMaterial(row) : null;

                    if (m != null)
                    {
                        m.SetColor("_ResourceColor", full);
                        m.SetColor("_ResourceColorEmpty", empty);
                    }

                    RawImage image = RowImage != null ? RowImage(row) : null;

                    if (image != null && image.texture != null)
                    {
                        Texture stock = StockOf(image.texture);
                        Texture want = index > 0 ? Solid(stock) : stock;

                        if (want != null && image.texture != want)
                            image.texture = want;
                    }

                    foreach (ResourceUnit unit in row.GetComponentsInChildren<ResourceUnit>(true))
                        PaintUnit(unit, v, index);
                }
            }

            LabelBar(bar, v, index);
        }

        // Made at the size it first shipped at and scaled down: a quarter of
        // it at 100% (R21 part 2, his "shrink it down to 1/4").
        private static void LabelBar(ResourceBar bar, View v, int index)
        {
            Label(v, bar.RectTransform, index, 0.62f, new Vector2(0.12f, 0.1f), new Vector2(2f, 0.8f),
                  NgMath.EnemyCountBase);
        }

        // ResourceBarRow.CreateUnit, postfix: a segment that just changed is
        // drawn by a unit for 1.5 s - in this layer's colours, not the game's.
        internal static void OnUnitCreated(ResourceBarRow row, ResourceUnit unit)
        {
            if (!_anyBars || row == null || unit == null)
                return;

            ResourceBar bar = row.GetComponentInParent<ResourceBar>();
            View v;

            if (bar == null || !_bars.TryGetValue(bar, out v) || v.Index <= 0)
                return;

            PaintUnit(unit, v, v.Index);
        }

        private static void PaintUnit(ResourceUnit unit, View v, int index)
        {
            if (unit == null)
                return;

            if (index <= 0)
            {
                // Back to the game's own: its colours, and its faint empty sprite.
                Sprite own;

                if (unit.emptySprite != null && _unitEmpty.TryGetValue(unit, out own))
                {
                    unit.emptySprite.sprite = own;
                    _unitEmpty.Remove(unit);
                }

                Tint(unit.fullSprite, v.Resource.resourceBarUnitColorFull);
                Tint(unit.emptySprite, v.Resource.resourceBarUnitColorEmpty);
                return;
            }

            Tint(unit.fullSprite, ColourOf(index, v.Resource));

            if (unit.emptySprite != null && unit.fullSprite != null)
            {
                Sprite own;

                if (!_unitEmpty.TryGetValue(unit, out own))
                    _unitEmpty.Add(unit, unit.emptySprite.sprite);

                unit.emptySprite.sprite = unit.fullSprite.sprite;
            }

            Tint(unit.emptySprite, ColourOf(index - 1, v.Resource));
        }

        // The colour, keeping the alpha the unit's animation owns.
        private static void Tint(SpriteRenderer sr, Color c)
        {
            if (sr != null)
                sr.color = new Color(c.r, c.g, c.b, sr.color.a);
        }

        // ---- the solid segment texture ----------------------------------------

        private static Texture StockOf(Texture shown)
        {
            Texture stock;
            return _stockOf.TryGetValue(shown, out stock) && stock != null ? stock : shown;
        }

        // The game's texture with its solid bottom half copied over its faint
        // top half. A GPU copy: the game's textures are not CPU-readable. If
        // the copy cannot be made, the stock texture stays (the rest of the
        // bar then shows the next layer's colour in the faint "empty" look).
        private static Texture Solid(Texture stock)
        {
            Texture2D made;

            if (stock == null)
                return null;

            if (_solid.TryGetValue(stock, out made) && made != null)
                return made;

            try
            {
                int w = stock.width, h = stock.height, half = h / 2;

                if (w <= 0 || half <= 0 ||
                    SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
                    throw new InvalidOperationException("this GPU cannot copy textures");

                made = new Texture2D(w, h, stock.graphicsFormat, TextureCreationFlags.None);
                made.name = stock.name + " (New Game Plus layer)";
                made.filterMode = stock.filterMode;
                made.wrapMode = stock.wrapMode;

                // Upload it once BEFORE the copies, so it surely has a GPU
                // texture to copy into - and never again after, or the blank
                // CPU side would overwrite them.
                made.Apply(false, false);

                Graphics.CopyTexture(stock, 0, 0, made, 0, 0);
                Graphics.CopyTexture(stock, 0, 0, 0, 0, w, half, made, 0, 0, 0, h - half);

                _solid[stock] = made;
                _stockOf[made] = stock;
                return made;
            }
            catch (Exception ex)
            {
                _solid[stock] = null;

                if (!_saidNoCopy)
                {
                    _saidNoCopy = true;
                    NewGamePlusPlugin.Log.LogWarning(
                        "health bars in layers: could not make the solid segment texture for " + stock.name +
                        " (" + ex.Message + "). The layers still work; the rest of a bar under a layer " +
                        "shows the next colour faintly instead of solid.");
                }

                return stock;
            }
        }

        // ---- the Queens' bar ----------------------------------------------------

        // BossHealthbar.Assign, prefix. HealthbarManager hands it the current
        // boss's Health tank as the boss state starts; the boss is asked of
        // the BossStateManager (the same one the manager reads).
        internal static ResourceTank WrapBoss(BossHealthbar bar, ResourceTank tank)
        {
            if (bar == null || tank == null)
                return tank;

            // Our own re-assign, when the layer size changed.
            if (_boss != null && tank == _boss.Shadow)
                return tank;

            _boss = null;
            _bossBar = null;

            if (NgRun.HealthPercent <= 0f || tank.resource == null)
                return tank;

            BossStateManager bosses;

            if (!ServiceLocator.TryGet<BossStateManager>(out bosses) || bosses == null)
                return tank;

            SavableEntity boss = bosses.CurrentBoss;

            if (boss == null || boss.EntityData == null)
                return tank;

            HealthbarOwner owner = boss.GetComponent<HealthbarOwner>();

            if (owner == null || owner.Unit == null || owner.Unit.GetTank(tank.resource) != tank)
                return tank;

            SayMissing();

            var v = new View
            {
                Real = tank,
                Resource = tank.resource,
                Id = boss.EntityData.instanceId,
                Kind = boss.EntityData.entityId,
            };
            v.Shadow = NewShadow(v);

            _boss = v;
            _bossBar = bar;
            return v.Shadow;
        }

        // BossHealthbar.Assign, postfix: the rows are new, in the game's
        // colours - paint on the next update.
        internal static void AfterBossAssign(BossHealthbar bar)
        {
            if (_boss != null && bar == _bossBar)
            {
                _boss.Index = -1;
                _boss.HaveEmptyStock = false;
            }
        }

        // BossHealthbar.Update, prefix.
        internal static void SyncBoss(BossHealthbar bar)
        {
            View v = _boss;

            if (v == null || bar == null || bar != _bossBar || v.Real == null)
                return;

            int index;
            float capacity, value;

            Compute(v, out index, out capacity, out value);

            if (Math.Abs(v.Shadow.Capacity - capacity) > 0.0001f)
            {
                v.Shadow.Capacity = capacity;
                v.Shadow.Value = value;
                bar.Assign(v.Shadow);           // new rows of the new size
            }

            v.Shadow.Value = value;

            if (v.LabelSized != CountSize())
            {
                if (v.Label != null)
                    Resize(v, index);
                else if (index > 0)
                    LabelBoss(bar, v, index);

                v.LabelSized = CountSize();
            }

            if (index > 0)
                Palette();

            if (index == v.Index)
            {
                // Same layer: only a palette changed in the tab repaints it.
                if (index > 0 && v.Painted != _paletteVersion)
                {
                    PaintBoss(bar, v, index);
                    v.Painted = _paletteVersion;
                }

                return;
            }

            PaintBoss(bar, v, index);
            v.Painted = _paletteVersion;

            // The white "recent damage" strip starts again inside this layer.
            if (BossDelta != null)
                BossDelta(bar) = value;

            if (index > 0 && !_saidBoss)
            {
                _saidBoss = true;
                NewGamePlusPlugin.Log.LogInfo(
                    "health bars in layers: the Queen's bar (" + v.Kind + ") shows " + (index + 1) +
                    " layers of " + Math.Round(capacity) + " health instead of one bar of " +
                    Math.Round(v.Real.Capacity) + ". Said once a world.");
            }

            v.Index = index;
        }

        private static void PaintBoss(BossHealthbar bar, View v, int index)
        {
            Color full = ColourOf(index, v.Resource);
            List<BossHealthbarRow> rows = BossRows != null ? BossRows(bar) : null;

            if (rows != null)
            {
                foreach (BossHealthbarRow row in rows)
                {
                    if (row == null)
                        continue;

                    Image fill = BossFill != null ? BossFill(row) : null;

                    if (fill != null)
                        fill.color = full;

                    Transform empty = row.transform.Find("Empty");
                    Image back = empty != null ? empty.GetComponent<Image>() : null;

                    if (back == null)
                        continue;

                    // Every row is fresh from the prefab after an Assign, so the
                    // first one painted still has the game's grey.
                    if (!v.HaveEmptyStock)
                    {
                        v.EmptyStock = back.color;
                        v.HaveEmptyStock = true;
                    }

                    back.color = index > 0 ? ColourOf(index - 1, v.Resource) : v.EmptyStock;
                }
            }

            LabelBoss(bar, v, index);
        }

        private static void LabelBoss(BossHealthbar bar, View v, int index)
        {
            Label(v, bar.transform as RectTransform, index, 30f, new Vector2(16f, 8f), new Vector2(160f, 44f),
                  NgMath.BossCountBase);
        }

        // ---- the "xN" -------------------------------------------------------------

        // Beside the bar's top right. A TextMeshPro made here, in the font of
        // the game's own HUD text (by type name - the house idiom). Hidden on
        // the last layer, and everywhere at HealthBarCountSize 0.
        //
        // ★ SIZED BY SCALE, NOT BY FONT SIZE: the label is made at the size
        // it first shipped at, and `localScale` (pivot at its top left, on the
        // bar's corner) takes it to HealthBarCountSize percent of the bar's own
        // default - so a tab change is one write, with no text re-layout.
        private static void Label(View v, RectTransform bar, int index, float size,
                                  Vector2 offset, Vector2 box, float baseScale)
        {
            if (bar == null || v == null)
                return;

            v.LabelBase = baseScale;
            string text = NgMath.LayerCountText(index);
            int percent = CountSize();

            try
            {
                if (v.Label == null)
                {
                    if (text.Length == 0 || percent <= 0)
                        return;

                    v.Label = MakeLabel(bar, size, offset, box);

                    if (v.Label == null)
                        return;

                    v.LabelSized = -1;
                }

                if (v.LabelSized != percent)
                {
                    float s = NgMath.CountScale(percent, baseScale);
                    v.Label.transform.localScale = new Vector3(s, s, 1f);
                    v.LabelSized = percent;
                }

                bool show = text.Length > 0 && percent > 0;

                if (v.Label.gameObject.activeSelf != show)
                    v.Label.gameObject.SetActive(show);

                if (show)
                    NgUi.SetText(v.Label, text);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        // The size changed in the tab (or the cfg): rescale, and show or hide.
        private static void Resize(View v, int index)
        {
            try
            {
                int percent = CountSize();
                float s = NgMath.CountScale(percent, v.LabelBase > 0f ? v.LabelBase : 1f);

                v.Label.transform.localScale = new Vector3(s, s, 1f);
                v.LabelSized = percent;

                string text = NgMath.LayerCountText(index);
                bool show = text.Length > 0 && percent > 0;

                if (v.Label.gameObject.activeSelf != show)
                    v.Label.gameObject.SetActive(show);

                if (show)
                    NgUi.SetText(v.Label, text);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        // `[Display] HealthBarCountSize`, read live (a property read a frame).
        private static int CountSize()
        {
            return NgSettings.HealthBarCountSize != null
                ? NgMath.ClampCountSize(NgSettings.HealthBarCountSize.Value)
                : NgMath.CountSizeDefault;
        }

        private static Component MakeLabel(RectTransform bar, float size, Vector2 offset, Vector2 box)
        {
            Transform old = bar.Find(LabelName);

            if (old != null)
                UnityEngine.Object.Destroy(old.gameObject);

            Type ugui = NgUi.FindTypeQuietly("TMPro.TextMeshProUGUI");

            if (ugui == null)
                return null;

            var go = new GameObject(LabelName, typeof(RectTransform));
            go.layer = bar.gameObject.layer;

            var rt = (RectTransform)go.transform;
            rt.SetParent(bar, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = box;
            rt.localScale = Vector3.one;

            Component t = go.AddComponent(ugui);

            FindFont();
            Set(t, "font", _font);
            Set(t, "fontSharedMaterial", _fontMaterial);
            Set(t, "fontSize", size);
            Set(t, "enableAutoSizing", false);
            Set(t, "color", new Color(1f, 1f, 1f, 0.95f));
            Set(t, "raycastTarget", false);
            SetEnum(t, "alignment", "TopLeft");
            SetEnum(t, "textWrappingMode", "NoWrap");
            Set(t, "enableWordWrapping", false);

            return t;
        }

        // The font of a live HUD text (the ship log's), else any live text.
        private static void FindFont()
        {
            if (_lookedForFont && _font != null)
                return;

            _lookedForFont = true;

            Type tmp = NgUi.FindTypeQuietly("TMPro.TMP_Text");

            if (tmp == null)
                return;

            object any = null, anyMaterial = null;

            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(tmp))
            {
                var c = o as Component;

                if (c == null || !c.gameObject.scene.IsValid())
                    continue;

                object font = Get(c, "font");

                if (font == null)
                    continue;

                if (any == null)
                {
                    any = font;
                    anyMaterial = Get(c, "fontSharedMaterial");
                }

                for (Transform p = c.transform; p != null; p = p.parent)
                {
                    if (p.name == "InGameHUD")
                    {
                        _font = font;
                        _fontMaterial = Get(c, "fontSharedMaterial");
                        return;
                    }
                }
            }

            _font = any;
            _fontMaterial = anyMaterial;
        }

        private static object Get(Component c, string property)
        {
            try
            {
                PropertyInfo p = c.GetType().GetProperty(property);
                return p != null ? p.GetValue(c, null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Set(Component c, string property, object value)
        {
            if (value == null)
                return;

            try
            {
                PropertyInfo p = c.GetType().GetProperty(property);

                if (p != null && p.CanWrite)
                    p.SetValue(c, value, null);
            }
            catch (Exception)
            {
            }
        }

        private static void SetEnum(Component c, string property, string name)
        {
            try
            {
                PropertyInfo p = c.GetType().GetProperty(property);

                if (p != null && p.CanWrite && p.PropertyType.IsEnum &&
                    Enum.IsDefined(p.PropertyType, name))
                {
                    p.SetValue(c, Enum.Parse(p.PropertyType, name), null);
                }
            }
            catch (Exception)
            {
            }
        }

        // Said once a session if a field this needs has gone (a game update).
        internal static void SayMissing()
        {
            if (_saidMissing || _missingFields.Length == 0)
                return;

            _saidMissing = true;
            NewGamePlusPlugin.Log.LogWarning(
                "health bars in layers: could not find " + _missingFields +
                " - the parts of the bars that need them stay as the game draws them.");
        }
    }

    // ---- the patches --------------------------------------------------------

    [HarmonyPatch(typeof(HealthbarWidget), "GenerateResourceBars")]
    internal static class NgLayersWidgetPatch
    {
        static void Prefix(HealthbarWidget __instance)
        {
            try
            {
                NgLayers.Generating = NgLayers.OwnerOf(__instance);
            }
            catch (Exception)
            {
                NgLayers.Generating = null;
            }
        }

        static Exception Finalizer(Exception __exception)
        {
            NgLayers.Generating = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ResourceBar), "Assign")]
    internal static class NgLayersAssignPatch
    {
        static void Prefix(ResourceBar __instance, ref ResourceTank resourceTank)
        {
            try
            {
                resourceTank = NgLayers.WrapBar(__instance, resourceTank);
            }
            catch (Exception)
            {
                // The game's own bar, unchanged.
            }
        }
    }

    [HarmonyPatch(typeof(ResourceBar), "Update")]
    internal static class NgLayersBarPatch
    {
        static void Prefix(ResourceBar __instance)
        {
            try
            {
                NgLayers.SyncBar(__instance);
            }
            catch (Exception)
            {
                // Cosmetic only; never break a bar.
            }
        }
    }

    [HarmonyPatch(typeof(ResourceBarRow), "CreateUnit")]
    internal static class NgLayersUnitPatch
    {
        static void Postfix(ResourceBarRow __instance, ResourceUnit __result)
        {
            try
            {
                NgLayers.OnUnitCreated(__instance, __result);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(BossHealthbar), "Assign")]
    internal static class NgLayersBossAssignPatch
    {
        static void Prefix(BossHealthbar __instance, ref ResourceTank resourceTank)
        {
            try
            {
                resourceTank = NgLayers.WrapBoss(__instance, resourceTank);
            }
            catch (Exception)
            {
            }
        }

        static void Postfix(BossHealthbar __instance)
        {
            try
            {
                NgLayers.AfterBossAssign(__instance);
            }
            catch (Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(BossHealthbar), "Update")]
    internal static class NgLayersBossPatch
    {
        static void Prefix(BossHealthbar __instance)
        {
            try
            {
                NgLayers.SyncBoss(__instance);
            }
            catch (Exception)
            {
            }
        }
    }
}
