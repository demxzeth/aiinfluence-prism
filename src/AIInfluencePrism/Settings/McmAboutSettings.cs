using System;
using AIInfluencePrism.UI;

namespace AIInfluencePrism.Settings
{
    /// <summary>
    /// MCM About & Single Informational Tab reference.
    /// Bridges the Settings namespace with UI.McmProviderSettings.
    /// Does not inherit from AttributeGlobalSettings to ensure MCM displays only ONE clean tab.
    /// </summary>
    public static class McmAboutSettings
    {
        public static McmProviderSettings Instance => McmProviderSettings.Instance;
    }
}
