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
                "A tiny marker above every enemy New Game Plus has buffed. Off by default. " +
                "Never a colour tint - the game's own hit flash owns an enemy's colour.");
        }
    }
}
