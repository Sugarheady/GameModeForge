using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameModeForge.NewGamePlus
{
    // THE ARITHMETIC OF NEW GAME PLUS, with no Unity and no game types in it,
    // so Harness/ngmathprobe.py can compile exactly this file and run it.
    //
    // Everything here is a number that decides what a world is like, and each
    // one was a decision rather than a guess (CONCEPT.md 2.4):
    //
    //   - a buff STACKS each plus: start + perPlus * (plus - fromPlus), up to
    //     its cap. "+50% health per plus" is +50%, +100%, +150%. No compounding.
    //   - nothing before `fromPlus`. fromPlus 0 means the first world too.
    //   - coverage (the share of enemies that get it) ramps the same way and
    //     can never pass 100%.
    //   - whether ONE enemy is covered is a roll keyed on NG+'s own seed and the
    //     enemy's instance id, so the same enemy gets the same answer after
    //     every Continue with nothing stored per enemy (2.6).
    public static class NgMath
    {
        // A buff's value (a percent) at `plus`. A cap of 0 or less is "no cap".
        public static float Value(bool enabled, int fromPlus, float start,
                                  float perPlus, float max, int plus)
        {
            if (!enabled || plus < fromPlus)
                return 0f;

            float v = start + perPlus * (plus - fromPlus);

            if (max > 0f && v > max)
                v = max;

            return v < 0f ? 0f : v;
        }

        // The share of enemies (a percent, 0-100) that get a buff at `plus`.
        public static float Coverage(bool enabled, int fromPlus, float coverageStart,
                                     float coveragePerPlus, int plus)
        {
            if (!enabled || plus < fromPlus)
                return 0f;

            float c = coverageStart + coveragePerPlus * (plus - fromPlus);

            if (c > 100f)
                c = 100f;

            return c < 0f ? 0f : c;
        }

        // ★ A WHOLE-NUMBER BUFF (enemy weapon stats, stage 2: extra projectiles
        // and burst shots). DECIDED 2026-10-05: "this wouldn't be a fraction,
        // it would need to be a whole number ... maybe 2 every other ngp. Let
        // this be adjustable." So: `add` more at `fromPlus`, and `add` more
        // again every `every` pluses after it, up to `max` (0 = no cap). His
        // "2 every other" is add 2, every 2: +2, +2, +4, +4, +6...
        public static int StepValue(bool enabled, int fromPlus, int add, int every, int max, int plus)
        {
            if (!enabled || plus < fromPlus || add <= 0)
                return 0;

            int steps = every <= 1 ? plus - fromPlus : (plus - fromPlus) / every;
            long v = (long)add * (1 + steps);

            if (max > 0 && v > max)
                v = max;

            return v > int.MaxValue ? int.MaxValue : (int)v;
        }

        // ★ THE STATION UNLOCK COST (stage 2, built 2026-10-05). DECIDED
        // (CONCEPT.md 2.3): the first station you pay for costs `first` (100),
        // each one after it `rise` more (300: 100, 400, 700...), the ladder
        // starts again in every new world, and the RISE grows by `risePerPlus`
        // each plus (100: a rise of 300 in the first world it is on, 400 in
        // the next...) - his shape, "start at 100 and somehow increase the
        // incremental portion". `paid` is how many this world has paid for
        // already; the station you start beside is never on the ladder.
        public static int StationRise(int rise, int risePerPlus, int fromPlus, int plus)
        {
            long r = (long)rise + (long)risePerPlus * Math.Max(0, plus - fromPlus);
            return r < 0 ? 0 : (r > int.MaxValue ? int.MaxValue : (int)r);
        }

        public static int StationPrice(int first, int rise, int risePerPlus, int fromPlus, int plus, int paid)
        {
            long v = (long)Math.Max(0, first) +
                     (long)StationRise(rise, risePerPlus, fromPlus, plus) * Math.Max(0, paid);
            return v > int.MaxValue ? int.MaxValue : (int)v;
        }

        // THE GAME'S INGREDIENTS (measured 2026-10-05: IngredientRegistry holds
        // nine) - id, then the name the game shows. Two show a different name
        // from their id: Coral is "Fiber", Shell is "Generator". A station cost
        // may name either. The game's own registry is still asked at run time,
        // so an ingredient a game update adds works by its id.
        public static readonly string[][] KnownIngredients =
        {
            new[] { "Bond", "Bond" }, new[] { "Chip", "Chip" }, new[] { "Coral", "Fiber" },
            new[] { "Ex", "Ex" }, new[] { "Face", "Face" }, new[] { "Gland", "Gland" },
            new[] { "Powerstar", "Powerstar" }, new[] { "Shell", "Generator" },
            new[] { "strange_ball", "Strange Ball" },
        };

        private static string Squash(string s)
        {
            return (s ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        }

        // The game id for a name or an id ("Fiber" -> "Coral"), or null.
        public static string IngredientId(string text)
        {
            string k = Squash(text);

            if (k.Length == 0)
                return null;

            foreach (string[] i in KnownIngredients)
                if (Squash(i[0]) == k || Squash(i[1]) == k)
                    return i[0];

            return null;
        }

        // The name the game shows for an id or a name; the text itself if unknown.
        public static string IngredientName(string text)
        {
            string id = IngredientId(text);

            foreach (string[] i in KnownIngredients)
                if (i[0] == id)
                    return i[1];

            return (text ?? "").Trim();
        }

        public static bool SameIngredient(string text, string id, string displayName)
        {
            string k = Squash(text);
            return k.Length > 0 && (k == Squash(id) || k == Squash(displayName));
        }

        // ★ AN INGREDIENT COST ON A STATION (his ask, 2026-10-05: "an option
        // to include resource cost to unlocking stations ... make it an option
        // to increase per station as well as just increasing said resource/s
        // per new game plus ... Default is none"). How many of one ingredient
        // the next station takes: `amount`, +`perStation` for each station
        // already paid for this world, +`perPlus` for each plus past
        // `fromPlus`, up to `max` (0 = no cap).
        public static int IngredientCost(int amount, int perStation, int perPlus, int max,
                                         int fromPlus, int plus, int paid)
        {
            long v = (long)amount + (long)perStation * Math.Max(0, paid) +
                     (long)perPlus * Math.Max(0, plus - fromPlus);

            if (max > 0 && v > max)
                v = max;

            return v < 0 ? 0 : (v > int.MaxValue ? int.MaxValue : (int)v);
        }

        // A percent bonus as the multiplier it becomes: +50 -> x1.5.
        public static float Multiplier(float percent)
        {
            return 1f + (percent < 0f ? 0f : percent) / 100f;
        }

        // ---- enemy damage (2026-10-02) -------------------------------------
        //
        // WHO A HIT CAME FROM, as NgDamage sorts it. Enemy damage % is on every
        // enemy (no coverage - his call) and on the world's own hazards, to
        // everything they hit; anything of yours is never touched; and a hit
        // whose source cannot be told is left alone, because this mod only ever
        // ADDS difficulty and every doubt resolves to "add nothing".
        public const int SideUnknown = 0;   // could not tell
        public const int SidePlayer = 1;    // you, your drones, minions, wingmen, a co-op partner
        public const int SideEnemy = 2;     // an enemy, bosses included
        public const int SideWorld = 3;     // hazard blocks, white bulbs, exploding plants, steam
        // A crate or box: nobody's (DECIDED 2026-10-02 - "I wouldn't count
        // crates as hazards please"). Left alone like yours and never counted.
        // Measured the same day: none of the 13 does any damage anyway.
        public const int SideNeutral = 4;

        // The multiplier a hit from `side` takes this world.
        public static float DamageScale(int side, float percent, bool environment)
        {
            if (side == SideEnemy)
                return Multiplier(percent);

            if (side == SideWorld && environment)
                return Multiplier(percent);

            return 1f;
        }

        // A beam's or an electric spark's target mask. Aimed at the Player
        // layer and not at Entities is an enemy's (every enemy laser ships
        // 9280); aimed at Entities and not at Player is yours (9344); both or
        // neither cannot be told.
        public static int SideFromMask(int mask, int playerLayer, int entitiesLayer)
        {
            if (playerLayer < 0 || playerLayer > 31 || entitiesLayer < 0 || entitiesLayer > 31)
                return SideUnknown;

            bool player = (mask & (1 << playerLayer)) != 0;
            bool entities = (mask & (1 << entitiesLayer)) != 0;

            if (player && !entities)
                return SideEnemy;

            if (entities && !player)
                return SidePlayer;

            return SideUnknown;
        }

        // A shot with no owner (a sub-emitter fragment): its GameObject layer
        // IS its faction.
        public static int SideFromShotLayer(int layer, int enemyShots, int playerShots)
        {
            if (layer >= 0 && layer == enemyShots)
                return SideEnemy;

            if (layer >= 0 && layer == playerShots)
                return SidePlayer;

            return SideUnknown;
        }

        // A number in [0, 1), the same for the same three inputs on every
        // machine and every launch. A plain integer mix (the finaliser of
        // murmur3), so it needs no System.Random - whose sequence is not
        // promised across .NET versions - and no state.
        public static float Roll(int seed, int id, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)id * 0x85EBCA77u;
                h ^= (uint)salt * 0xC2B2AE3Du;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;

                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        // Is this enemy one of the covered share?
        public static bool Covered(int seed, int id, int salt, float coveragePercent)
        {
            if (coveragePercent >= 100f)
                return true;

            if (coveragePercent <= 0f)
                return false;

            return Roll(seed, id, salt) * 100f < coveragePercent;
        }

        // ★ THE STATIONS-UNLOCKED COUNT ACROSS A JUMP. `RunData.Initialize`
        // itself counts 1 (the starter shop tier) before any station, so a
        // fresh world's count is 1 + whatever it has unlocked by the time the
        // carry is applied (its start station). Carrying the old count
        // wholesale would lose the new world's start station; adding the two
        // would count the starter tier twice.
        public static int MergeStationCount(int carried, int fresh)
        {
            return carried + Math.Max(0, fresh - 1);
        }

        // ---- health bars in layers (stage 2, built 2026-10-05) ---------------
        //
        // His Street Fighter idea: a buffed enemy's bar stays its NORMAL size,
        // and the extra health is stacked in layers of that size, each its own
        // colour. DECIDED 2026-10-05:
        //   - one layer is the enemy's normal health (what it had before New
        //     Game Plus multiplied it - in co-op, the co-op normal);
        //   - the PARTIAL layer is the TOP one, so every layer under it is a
        //     whole bar and the last one is exactly the bar the game drew;
        //   - the last layer keeps the enemy's own colour; the layers above it
        //     count up green, yellow, orange, red, purple, then round again.
        //
        // Layers are counted from the LAST one: index 0 is the enemy's normal
        // bar, 1 the layer above it, and so on.
        //
        // ★ A FLOAT SLACK, NOT AN EXACT DIVIDE. Capacity is normal x a
        // multiplier, and normal is capacity / that multiplier again, so
        // capacity / layer lands a hair either side of a whole number: 2.0000001
        // must be two layers, not three.
        private const float LayerSlack = 0.0001f;

        public static int LayerCount(float capacity, float layer)
        {
            if (layer <= 0f || capacity <= layer)
                return 1;

            int n = (int)Math.Ceiling(capacity / layer - LayerSlack);
            return n < 1 ? 1 : n;
        }

        // Which layer `value` is in. Exactly on a boundary is the FULL layer
        // below it (1600 of 3200 at a layer of 1600 is a full last bar).
        public static int LayerIndex(float value, float capacity, float layer)
        {
            int n = LayerCount(capacity, layer);

            if (n <= 1 || value <= 0f)
                return 0;

            int k = (int)Math.Ceiling(value / layer - LayerSlack) - 1;

            if (k < 0)
                k = 0;

            return k > n - 1 ? n - 1 : k;
        }

        // What the bar shows inside layer `index`: 0 to one layer.
        public static float InLayer(float value, int index, float layer)
        {
            if (layer <= 0f)
                return value < 0f ? 0f : value;

            float v = value - index * layer;

            if (v < 0f)
                return 0f;

            return v > layer ? layer : v;
        }

        // ---- the layers' colours (his ask, 2026-10-05: "a setting that the
        // player can change which colors those layers are") ------------------
        //
        // DECIDED: a row in the GAME MODE FORGE tab steps through ready-made
        // palettes, the last of them CUSTOM - his own list in the cfg, any
        // number of colours, by name or hex - and the last layer is ALWAYS the
        // enemy's own colour, whatever the palette ("Keep own colour last").
        //
        // The palettes are hex, so the builder page's copy can be compared with
        // these exactly. CLASSIC is the look the layers were built with.
        public static readonly string[] PaletteNames =
            { "CLASSIC", "COLOUR-SAFE", "ICE", "FIRE", "NEON", "CUSTOM" };

        public const int CustomPalette = 5;

        public static readonly string[][] PaletteHex =
        {
            new[] { "#45DB4D", "#FFDB1F", "#FF8514", "#ED2121", "#A852FF" },  // green yellow orange red purple
            new[] { "#E69F00", "#56B4E9", "#009E73", "#F0E442", "#0072B2" },  // Okabe-Ito: told apart with any colour vision
            new[] { "#F2FAFF", "#4DE6FF", "#3380FF", "#594DE6", "#A666FF" },  // white cyan blue indigo violet
            new[] { "#FFEB4D", "#FFBF1A", "#FF8514", "#ED2121", "#990D26" },  // yellow gold orange red crimson
            new[] { "#99FF1A", "#1AFFFF", "#FF33E6", "#FFFF33", "#FF73B3" },  // lime cyan magenta yellow pink
        };

        public const string DefaultCustomColours = "green, yellow, orange, red, purple";

        // The names a custom list may use. Spaces, hyphens and underscores are
        // ignored, so "sky blue", "sky-blue" and "SkyBlue" are one name.
        private static readonly Dictionary<string, string> NamedColours = new Dictionary<string, string>
        {
            { "green", "#45DB4D" }, { "yellow", "#FFDB1F" }, { "orange", "#FF8514" },
            { "red", "#ED2121" }, { "purple", "#A852FF" }, { "blue", "#3380FF" },
            { "cyan", "#4DE6FF" }, { "skyblue", "#56B4E9" }, { "sky", "#56B4E9" },
            { "teal", "#1AB3A6" }, { "lime", "#99FF1A" }, { "gold", "#FFBF1A" },
            { "crimson", "#990D26" }, { "pink", "#FF73B3" }, { "magenta", "#FF33E6" },
            { "indigo", "#594DE6" }, { "violet", "#A666FF" }, { "white", "#FFFFFF" },
            { "silver", "#C8C8C8" }, { "grey", "#9E9E9E" }, { "gray", "#9E9E9E" },
            { "brown", "#8C5A2B" }, { "navy", "#1A2E80" }, { "black", "#1A1A1A" },
        };

        public const int MaxCustomColours = 12;

        // One colour as 0-1 RGB: a name, #RRGGBB, RRGGBB, #RGB or RGB. Null if
        // it is none of those.
        public static float[] ParseColour(string text)
        {
            if (text == null)
                return null;

            string s = text.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

            if (s.Length == 0)
                return null;

            string hex;

            if (NamedColours.TryGetValue(s, out hex))
                s = hex.Substring(1).ToLowerInvariant();
            else if (s[0] == '#')
                s = s.Substring(1);

            if (s.Length == 3)
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });

            int rgb;

            if (s.Length != 6 ||
                !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
            {
                return null;
            }

            return new[] { ((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f };
        }

        // A custom list: commas or semicolons between colours. What cannot be
        // read is skipped and named in `bad`; past MaxCustomColours, the rest
        // is dropped and said in `bad` too.
        public static float[][] ParseColourList(string text, List<string> bad)
        {
            var found = new List<float[]>();

            if (text == null)
                return found.ToArray();

            foreach (string part in text.Split(',', ';'))
            {
                string p = part.Trim().Trim('"', '\'');

                if (p.Length == 0)
                    continue;

                float[] c = ParseColour(p);

                if (c == null)
                {
                    if (bad != null)
                        bad.Add(p);

                    continue;
                }

                if (found.Count >= MaxCustomColours)
                {
                    if (bad != null)
                        bad.Add(p + " (more than " + MaxCustomColours + ")");

                    continue;
                }

                found.Add(c);
            }

            return found.ToArray();
        }

        public static float[][] PaletteColours(int palette)
        {
            if (palette < 0 || palette >= PaletteHex.Length)
                palette = 0;

            var cols = new float[PaletteHex[palette].Length][];

            for (int i = 0; i < cols.Length; i++)
                cols[i] = ParseColour(PaletteHex[palette][i]);

            return cols;
        }

        // ★ NEVER TWO LIKE COLOURS IN A ROW. The layer under a layer must be
        // told apart from it - a Caps drone's own orange under COLOUR-SAFE's
        // orange, or a custom "red" over a red bar, would read as one bar. A
        // colour this close to the one below it is skipped for the next.
        public const float CloseColour = 0.2f;

        public static bool CloseColours(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3)
                return false;

            float dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
            return Math.Sqrt(dr * dr + dg * dg + db * db) < CloseColour;
        }

        // The colour of layer `index`: -1 is the enemy's own colour (the last
        // layer, always), otherwise a slot in the palette. The layers above
        // the last take the palette in order and go round again, skipping a
        // colour too like the one below it (when there is any other).
        public static int LayerSlot(int index, float[][] palette, float[] own)
        {
            if (index <= 0 || palette == null || palette.Length == 0)
                return -1;

            int n = palette.Length;
            float[] below = own;
            int slot = -1;

            for (int k = 1; k <= index; k++)
            {
                int s = (slot + 1) % n;

                for (int tries = 0; tries < n - 1 && CloseColours(palette[s], below); tries++)
                    s = (s + 1) % n;

                slot = s;
                below = palette[s];
            }

            return slot;
        }

        // The "x3" beside a layered bar: the layers left, this one included.
        // Nothing on the last layer - that is the enemy's normal bar.
        public static string LayerCountText(int index)
        {
            return index <= 0 ? "" : "x" + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ---- the xN's size ------------------------------------------------
        //
        // R21 part 2, his note: "the 2x bubble is pretty big, can you shrink it
        // down to 1/4 the size? and maybe add a option for the player to change
        // the scale of that?" A percent, `[Display] HealthBarCountSize`: 100 is
        // the default, 0 hides it. On an enemy's bar 100 is a QUARTER of the
        // size it first shipped at (EnemyCountBase), so 400 is that first size;
        // on the Queens' bar 100 is its first size (nobody asked for that one
        // to change). The tab steps it by 25.
        public const int CountSizeDefault = 100;
        public const int CountSizeMax = 400;
        public const int CountSizePerStep = 25;
        public const int CountSizeSteps = CountSizeMax / CountSizePerStep;
        public const float EnemyCountBase = 0.25f;
        public const float BossCountBase = 1f;

        public static int ClampCountSize(int percent)
        {
            return percent < 0 ? 0 : (percent > CountSizeMax ? CountSizeMax : percent);
        }

        // The label's scale: the percent of a bar's own default size.
        public static float CountScale(int percent, float baseScale)
        {
            return ClampCountSize(percent) / 100f * baseScale;
        }

        // The tab's caption for a size.
        public static string CountSizeText(int percent)
        {
            int p = ClampCountSize(percent);
            return p == 0
                ? "Layer count: hidden"
                : "Layer count size " + p.ToString(CultureInfo.InvariantCulture) + "%";
        }

        // The in-game sliders: a step index and the percent it stands for.
        public static int SliderPercent(int step, int percentPerStep)
        {
            return Math.Max(0, step) * percentPerStep;
        }

        public static int SliderStep(float percent, int percentPerStep, int steps)
        {
            if (percentPerStep <= 0)
                return 0;

            int s = (int)Math.Round(percent / percentPerStep, MidpointRounding.AwayFromZero);

            return s < 0 ? 0 : (s > steps ? steps : s);
        }

        // "+50%" -> "+50 percent", for a font that cannot draw "%" (NgUi.Drawable,
        // R21 part 2, 307). A "%" inside a rich-text tag (<size=50%>) is left
        // alone - it is never drawn.
        public static string PercentAsWord(string s)
        {
            if (s == null || s.IndexOf('%') < 0)
                return s;

            var sb = new System.Text.StringBuilder(s.Length + 16);
            bool inTag = false;

            foreach (char c in s)
            {
                if (c == '<')
                    inTag = true;
                else if (c == '>')
                    inTag = false;

                if (c != '%' || inTag)
                {
                    sb.Append(c);
                    continue;
                }

                if (sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n')
                    sb.Append(' ');

                sb.Append("percent");
            }

            return sb.ToString();
        }

        // "+50%", "+12.5%" - never "50.0000001".
        public static string Pct(float percent)
        {
            return "+" + Math.Round(percent, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        // What a world is called: the first world is WORLD 1, the one after the
        // first jump is NEW GAME PLUS 1 (DECIDED 2026-09-29 - the victory title,
        // the arrival alert and WORLD INFO all use this one name).
        public static string WorldName(int plus)
        {
            return plus <= 0
                ? "WORLD 1"
                : "NEW GAME PLUS " + plus.ToString(CultureInfo.InvariantCulture);
        }

        // ---- the first-world switch (stage 2, his idea 2026-10-02) ----------
        //
        // DECIDED: "Moving the chain up a whole step sounds correct if this is
        // toggled on" - the first world plays by NEW GAME PLUS 1's rules, NEW
        // GAME PLUS 1 by NEW GAME PLUS 2's, and so on. The titles keep counting
        // real worlds (WORLD 1 CLEARED); the arrival line and WORLD INFO say
        // whose rules are in force.
        public static int RulesPlus(int plus, int ahead)
        {
            return Math.Max(0, plus) + Math.Max(0, ahead);
        }

        // "WORLD 1", or "WORLD 1 (NEW GAME PLUS 1's rules)" with the switch on.
        public static string WorldWithRules(int plus, int ahead)
        {
            return ahead > 0
                ? WorldName(plus) + " (" + WorldName(RulesPlus(plus, ahead)) + "'s rules)"
                : WorldName(plus);
        }

        // ---- money drops (stage 2, DECIDED 2026-09-29 / 2026-10-02) -----------
        //
        // "More pickups, using the game's own 10 / 20 / 50", +25% a plus, no
        // default cap, every source with no roll. The fraction is a chance of
        // one more pickup: a pile of 3 at +25% is 3.75 pickups, so 3 and a 75%
        // chance of a fourth. `roll` is in [0, 1).
        public static int MoneyExtra(int count, float percent, double roll)
        {
            if (count <= 0 || percent <= 0f)
                return 0;

            double want = count * (percent / 100.0);
            int whole = (int)Math.Floor(want);
            double frac = want - whole;

            return whole + (roll < frac ? 1 : 0);
        }

        // ★ BIG COINS (R21 part 4, 356: "a massive lag spike ... 500% ... the
        // queen ... took a solid 3 minutes to unfreeze" - his suggestion: "a
        // new money object that holds more money"). Measured: the money
        // pickups sit on physics layer 14, whose collision row includes 14
        // ITSELF, and every pickup of a drop is made at one point - so a Level 2
        // crate's 100 coins at +500% were 600 overlapping bodies (about
        // 180,000 contact pairs) and the Queen's 200 were 1,200.
        //
        // So: extra pickups as copies of the pile only up to the pile's own
        // size (at most double the coins on the floor) and never more than
        // MaxMoneyCopies; the money past that comes as a few BIG COINS. The
        // money is the same either way.
        public const int MaxMoneyCopies = 50;
        public const int MaxBigCoins = 20;

        public static int MoneyCopyCap(int pileCount)
        {
            return Math.Max(0, Math.Min(pileCount, MaxMoneyCopies));
        }

        public static readonly int[] BigCoinValues =
            { 100000, 50000, 25000, 10000, 5000, 2500, 1000, 500, 250, 100 };

        // `value` split into big coins, largest first (greedy), at most
        // MaxBigCoins of them - the last one takes whatever is left over the
        // cap. `rest` is what is too small for a big coin (under 100), paid in
        // the pile's own coins.
        public static List<int> BigCoins(int value, out int rest)
        {
            var coins = new List<int>();
            rest = Math.Max(0, value);

            for (int i = 0; i < BigCoinValues.Length && rest >= BigCoinValues[BigCoinValues.Length - 1]; i++)
            {
                int v = BigCoinValues[i];

                while (rest >= v && coins.Count < MaxBigCoins)
                {
                    coins.Add(v);
                    rest -= v;
                }
            }

            // Past the cap: the last coin carries the remainder that is still
            // big-coin sized, so the money is never lost.
            if (coins.Count == MaxBigCoins && rest >= BigCoinValues[BigCoinValues.Length - 1])
            {
                coins[coins.Count - 1] += rest;
                rest = 0;
            }

            return coins;
        }

        // How much bigger a big coin is drawn than the game's 50 it copies.
        public static float BigCoinScale(int amount)
        {
            if (amount >= 10000) return 1.75f;
            if (amount >= 5000) return 1.6f;
            if (amount >= 2500) return 1.5f;
            if (amount >= 1000) return 1.4f;
            if (amount >= 500) return 1.3f;
            if (amount >= 250) return 1.2f;
            return 1.1f;
        }

        // Where a drop table's money comes from, by the table's own name
        // (measured: 44 tables name a money pickup). The order matters: the
        // Queen's table is "DropTable Unit Queen".
        public const int MoneyEnemies = 0;
        public const int MoneyQueens = 1;
        public const int MoneyCrates = 2;
        public const int MoneyWorld = 3;

        public static readonly string[] MoneySourceNames = { "enemies", "Queens", "crates and boxes", "the world" };

        public static int MoneySource(string tableName)
        {
            string n = tableName ?? "";

            if (n.IndexOf("Queen", StringComparison.OrdinalIgnoreCase) >= 0)
                return MoneyQueens;

            if (n.StartsWith("DropTable Box", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("DropTable Crate", StringComparison.OrdinalIgnoreCase))
                return MoneyCrates;

            if (n.StartsWith("DropTable Unit", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("DropTable Turret", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("DropTable Crawler", StringComparison.OrdinalIgnoreCase))
                return MoneyEnemies;

            // CellType GoldDropTable (gold in the rock), WheatDropTable (the
            // Wheat plant), and anything else.
            return MoneyWorld;
        }

        // ---- harder enemies earlier (stage 2, DECIDED 2026-10-02 / 2026-10-05) --
        //
        // Both dials are a percent of the way, 0 - 100, taken as a fraction.
        public static float DialFraction(float percent)
        {
            return percent <= 0f ? 0f : (percent >= 100f ? 1f : percent / 100f);
        }

        // DIAL A: a room's budget is lerp(close, far, distance from the centre).
        // Moving the CLOSE end f of the way toward the far end moves every room
        // f of the way to the outer rooms' budget: lerp(lerp(close, far, f),
        // far, d) = lerp(close, far, d + f(1 - d)) - and leaves the distance
        // itself alone, so the outer-rooms-only lines (dial B) are untouched.
        public static float NearEnd(float close, float far, float fraction)
        {
            return close + (far - close) * fraction;
        }

        // DIAL B: an "outer rooms only" line (0 - 1 from the centre) moved f of
        // the way to the centre; at 100% it is gone.
        public static float OuterLine(float minDistance, float fraction)
        {
            return minDistance <= 0f ? 0f : minDistance * (1f - fraction);
        }

        // His enemy table: a kind may appear in a biome from this plus on.
        // A plus below 0 in the file is "never".
        public static bool TableOpen(int fromPlus, int plus)
        {
            return fromPlus >= 0 && plus >= fromPlus;
        }

        // An ecosystem's name as the table writes it: "EcoSys Flesh" -> "Flesh".
        public static string BiomeName(string ecosystemName)
        {
            string n = (ecosystemName ?? "").Trim();
            return n.StartsWith("EcoSys ", StringComparison.Ordinal) ? n.Substring(7) : n;
        }

        // ★ HIS ASK (2026-10-06): EVERY enemy in the table, the Queen included -
        // "Just set their weight to 0 so they dont spawn naturally. Just want
        // the option." A Queen is ALLOWED now; it is only said, because every
        // Queen killed counts toward the 4 (raising that count when one comes
        // from the table is a later talk, no code).
        public static bool IsQueen(string kind)
        {
            return (kind ?? "").IndexOf("Queen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The budget a kind with no home biome costs when the table brings it
        // in and its row names no cost. The game's own costs run 1 to 20 (100
        // on one weight-0 entry); its units' own powerLevel is 10 on nearly
        // every kind, so 10 it is.
        public const int DefaultTablePower = 10;
    }
}
