using System;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// Former MCM Scope Settings. All scope settings are now unified in-game
    /// in the Imperial Archive WebUI accessible via Esc -> Prism Settings.
    /// Retained as a lightweight bridge for backward compatibility.
    /// </summary>
    public sealed class McmScopeSettings
    {
        private static readonly McmScopeSettings _instance = new McmScopeSettings();
        public static McmScopeSettings Instance => _instance;

        public void PullFromProfile()
        {
        }
    }
}
