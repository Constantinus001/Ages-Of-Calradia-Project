using System;
using TaleWorlds.Engine;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Bounded native-audio boundary for editor confirmation. A missing UI
    /// event disables later attempts and never prevents the authored border
    /// from being committed or saved.
    /// </summary>
    internal static class PoliticalBorderEditorSounds
    {
        private const string BorderPlacedEvent =
            "event:/ui/notification/quest_update";
        private static bool _disabled;

        internal static void PlayBorderPlaced()
        {
            if (_disabled) return;
            try
            {
                SoundEvent.PlaySound2D(BorderPlacedEvent);
            }
            catch (Exception exception)
            {
                _disabled = true;
                PoliticalBorderEditorDiagnostics.Error(
                    "Border placement sound failed; editor audio was disabled while drawing remains active.",
                    exception);
            }
        }
    }
}
