using System;

namespace AIInfluencePrism.Infra
{
    /// <summary>
    /// Constants of the mod: names, identifiers, file names.
    /// Collected in one place so no string is duplicated anywhere.
    /// </summary>
    public static class PrismInfo
    {
        /// <summary>Display name of the mod.</summary>
        public const string DisplayName = "AIInfluence Prism";

        /// <summary>Module id in the launcher.</summary>
        public const string ModuleId = "AIInfluencePrism";

        /// <summary>Human-readable version for About / diagnostics.</summary>
        public const string VersionText = "v0.1.0";

        /// <summary>Id of the Harmony patches.</summary>
        public const string HarmonyId = "aiinfluence.prism.hooks";

        /// <summary>Prefix of in-game chat messages: "[Prism - Dialogue] ...".</summary>
        public const string ChatPrefix = "Prism";

        /// <summary>Log file name, relative to the mod root.</summary>
        public const string LogRelativePath = "logs\\prism_log.txt";

        /// <summary>Configuration folder, relative to the mod root.</summary>
        public const string ConfigFolderName = "Config";

        /// <summary>Connection profiles file name.</summary>
        public const string ProfilesFileName = "prism_profiles.json";

        /// <summary>Nexus Mods page URL.</summary>
        public const string NexusUrl = "https://www.nexusmods.com/mountandblade2bannerlord/mods/13843";

        /// <summary>GitHub repository URL.</summary>
        public const string GitHubUrl = "https://github.com/demxzeth/aiinfluence-prism";

        /// <summary>Discord contact for support.</summary>
        public const string DiscordContact = "@demxzeth";
    }
}
