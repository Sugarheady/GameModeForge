using BepInEx.Configuration;

namespace GameModeForge.NewGamePlus
{
    // THE SWITCHES - New Game Plus's own BepInEx cfg,
    // `BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.cfg`.
    //
    // What carries is DECIDED (CONCEPT.md 2.3): "ideally everything", each
    // with its own switch, every one ON by default - except the new world's
    // health / fuel / ammo, which REFILL by default, and the enemy marker,
    // which is opt-in and OFF by default (2.4).
    //
    // ★ READ AT THE JUMP, NOT AT STARTUP. Every switch is read the moment you
    // leave a world, so a change made mid-run (the GAME MODE FORGE tab from the
    // pause menu, or this file by hand - BepInEx follows it) applies to the
    // next jump with no restart. The marker is read when an enemy appears.
    //
    // ★ BOUND BEFORE THE GATE. With New Game Plus switched off these entries
    // still exist, so Game Mode Forge's tab can show and change them for the
    // launch that turns it back on. Binding a setting patches nothing.
    internal static class NgSettings
    {
        public static ConfigEntry<bool> CarryGrid;
        public static ConfigEntry<bool> CarryVault;
        public static ConfigEntry<bool> CarryMoney;
        public static ConfigEntry<bool> CarryShop;
        public static ConfigEntry<bool> CarryPrices;
        public static ConfigEntry<bool> CarryStations;
        public static ConfigEntry<bool> CarryIngredients;
        public static ConfigEntry<bool> CarryTotals;
        public static ConfigEntry<bool> KeepResources;
        public static ConfigEntry<bool> EnemyMarker;
        public static ConfigEntry<bool> HealthBarLayers;
        public static ConfigEntry<NgLayerPalette> HealthBarPalette;
        public static ConfigEntry<string> HealthBarLayerColours;
        public static ConfigEntry<int> HealthBarCountSize;

        private const string Carry = "Carry to the next world";

        // Fully qualified: the game declares its own global `ConfigFile`
        // (the base of ConfigRegistry), and a global type beats a `using`.
        public static void Bind(BepInEx.Configuration.ConfigFile cfg)
        {
            CarryGrid = cfg.Bind(Carry, "ShipGrid", true,
                "Your ship's grid - its slot layout AND every installed module (weapons, " +
                "gadgets, upgrades). Off: the new world starts you on your starting loadout. " +
                "In co-op both ships carry, each to the same player.");

            CarryVault = cfg.Bind(Carry, "Vault", true,
                "The vault: spare modules, ingredients and consumables.");

            CarryMoney = cfg.Bind(Carry, "Money", true,
                "Your money. Money is also the run's score.");

            CarryShop = cfg.Bind(Carry, "ShopStock", true,
                "What the shop has discovered. Off resets the stock to the game's starting items.");

            CarryPrices = cfg.Bind(Carry, "ShopPrices", true,
                "Shop prices that rose from repeat buys. Off puts every carried item back at its " +
                "base price. Only matters with ShopStock on.");

            CarryStations = cfg.Bind(Carry, "StationsUnlockedCount", true,
                "The count of stations you have unlocked, which also sets the shop's tier. The " +
                "new world's stations themselves are new and locked - you unlock each one by " +
                "using it, and from then on it does fast travel, fuel and the shop.");

            CarryIngredients = cfg.Bind(Carry, "IngredientsEverOwned", true,
                "The ingredients you have ever held - the shop's unlock gate for some items.");

            CarryTotals = cfg.Bind(Carry, "RunTimeAndKills", true,
                "The run's total time and enemies killed, so they keep counting across worlds.");

            KeepResources = cfg.Bind("New world", "KeepHealthFuelAmmo", false,
                "Off (the default): a new world starts you on full health, fuel and ammo. On: " +
                "you arrive with what you had when you left.");

            EnemyMarker = cfg.Bind("Display", "EnemyMarker", false,
                "A small amber ring with a plus in it beside every enemy New Game Plus has " +
                "buffed. Off by default. " +
                "Never a colour tint - the game's own hit flash owns an enemy's colour.");

            // Stage 2's first piece (2026-10-05), DECIDED: layers for the
            // Queens and every enemy. A display setting, read live.
            HealthBarLayers = cfg.Bind("Display", "HealthBarLayers", true,
                "On (the default): a buffed enemy's health bar stays its normal size and its extra " +
                "health is stacked in layers of that size - the last layer is the enemy's own bar " +
                "and colour, the ones above it go green, yellow, orange, red, purple, then round " +
                "again, with an xN beside the bar counting the layers left. The Queens' bar too. " +
                "Off: the game's own bars, which grow with the health. Applies at once.");

            // His ask the same day: "a setting that the player can change which
            // colors those layers are". DECIDED: palettes in the tab, the last
            // one his own list; the last layer always the enemy's own colour.
            HealthBarPalette = cfg.Bind("Display", "HealthBarPalette", NgLayerPalette.Classic,
                "The colours of the layers ABOVE the last one (the last layer is always the " +
                "enemy's own bar colour). Classic: green, yellow, orange, red, purple. ColourSafe: " +
                "orange, sky blue, green, yellow, blue - told apart with any colour vision. Ice: " +
                "white, cyan, blue, indigo, violet. Fire: yellow, gold, orange, red, crimson. " +
                "Neon: lime, cyan, magenta, yellow, pink. Custom: your own list, " +
                "HealthBarLayerColours below. The layers take the colours in order and go round " +
                "again; a colour too like the one under it is skipped. Applies at once.");

            HealthBarLayerColours = cfg.Bind("Display", "HealthBarLayerColours", NgMath.DefaultCustomColours,
                "Your own layer colours, used when HealthBarPalette is Custom: up to " +
                NgMath.MaxCustomColours + ", separated by commas, the layer just above the last " +
                "first. Each one a name (green, yellow, orange, red, purple, blue, cyan, sky blue, " +
                "teal, lime, gold, crimson, pink, magenta, indigo, violet, white, silver, grey, " +
                "brown, navy, black) or a hex colour like #FF8800. New Game Plus Builder.html " +
                "makes this line with colour pickers. One it cannot read is skipped and named in " +
                "the log; with none it can read, Classic is used.");

            // R21 part 2, his note: "the 2x bubble is pretty big, can you
            // shrink it down to 1/4 the size? and maybe add a option for the
            // player to change the scale of that?" 100 is the new size, a
            // quarter of the first one; 400 is the first one.
            HealthBarCountSize = cfg.Bind("Display", "HealthBarCountSize", NgMath.CountSizeDefault,
                "How big the xN beside a layered health bar is, in percent. 100 (the default) is a " +
                "quarter of the size it first shipped at; 400 is that first size; 0 hides it. The " +
                "Queens' xN follows the same percent. Applies at once.");
        }
    }

    // The layer palettes, in NgMath.PaletteNames order (the tab steps through
    // them by number - pinned).
    public enum NgLayerPalette
    {
        Classic,
        ColourSafe,
        Ice,
        Fire,
        Neon,
        Custom,
    }
}
