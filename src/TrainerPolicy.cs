using System;

namespace DawnTrainer
{
    internal enum TrainerCommand
    {
        None,
        CompleteSelection,
        KillSelection,
        GiveResource
    }

    // Pure decisions shared by the runtime and regression checks; no Unity runtime needed.
    internal static class TrainerPolicy
    {
        internal static bool AllowsHotkeys(bool worldReady, bool overlayBlocksInput, bool interactive,
            bool realPaused, bool modalOpen, bool keyboardFocused)
        {
            return worldReady && !overlayBlocksInput && interactive && !realPaused && !modalOpen && !keyboardFocused;
        }

        internal static TrainerCommand SelectCommand(bool allowed, bool modified,
            bool buildEnabled, bool killEnabled, bool giveEnabled,
            bool insertDown, bool deleteDown, bool f10Down)
        {
            if (!allowed || modified) return TrainerCommand.None;
            if (killEnabled && deleteDown) return TrainerCommand.KillSelection;
            if (buildEnabled && insertDown) return TrainerCommand.CompleteSelection;
            if (giveEnabled && f10Down) return TrainerCommand.GiveResource;
            return TrainerCommand.None;
        }

        internal static bool CompleteBuild(bool alreadyBuilt, Func<bool> complete, Action recordKnowledge)
        {
            if (alreadyBuilt || !complete()) return false;
            recordKnowledge();
            return true;
        }
    }
}
