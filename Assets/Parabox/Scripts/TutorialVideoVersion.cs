namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V11 replaces per-mechanic interruptions with one three-skill mini-game per chapter.
        // Existing players receive each improved chapter video once without losing progress.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V11.Seen.";
    }
}
