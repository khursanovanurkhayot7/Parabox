namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V9 replaces the abstract mechanic card with solver-proven gameplay mini-puzzles. Players
        // who saw the old card receive each real tutorial once without resetting campaign progress.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V9.Seen.";
    }
}
