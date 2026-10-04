using System;

namespace GameModeForge
{
    // WHEN a mode's switch is read. This is per-mode on purpose: "does a
    // toggle apply mid-run or only at run entry" is an OPEN DESIGN QUESTION,
    // and making it a property of each rule rather than one global answer is
    // the honest way to hold a question open in code.
    //
    // Run entry is the cheaper answer and should stay the default. Nothing
    // here decides the question for the whole mod.
    public enum ForgeModeScope
    {
        // Latched when a run begins. Toggling mid-run changes nothing until
        // the next run, which is what most world rules want: a rule that can
        // appear halfway through a run has to describe what happens to the
        // world it did not generate.
        RunEntry,

        // Read every time the rule is consulted, so a toggle takes effect at
        // once. Correct for a rule with no world state behind it, and a trap
        // for one that has any.
        Live,
    }

    // One toggleable world rule.
    //
    // A mode is DECLARED at plugin load and ENABLED by the player. Those are
    // different facts and this class keeps them apart on purpose - see the
    // header of ForgeModeRegistry for why that split is the whole point of
    // this mod's architecture.
    public sealed class ForgeMode
    {
        // Stable, lowercase, no spaces. This is what the BepInEx config file
        // and any menu key off, so renaming one silently un-sets whatever the
        // player had chosen. Treat it as permanent once shipped.
        public readonly string Id;

        // What a human sees. Free to change.
        public readonly string DisplayName;

        // One sentence, shown in the config file and (later) the menu. It has
        // to say what the rule DOES, not what it is called.
        public readonly string Description;

        public readonly ForgeModeScope Scope;

        // What the player asked for. Set from the config at load and from the
        // menu later; it is NOT "is this rule in force" - ask the registry.
        internal bool Enabled;

        internal ForgeMode(string id, string displayName, string description,
                           ForgeModeScope scope)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("a mode needs an id", "id");

            Id = id;
            DisplayName = displayName ?? id;
            Description = description ?? "";
            Scope = scope;
        }

        public override string ToString()
        {
            return Id + " (" + Scope + ")";
        }
    }
}
