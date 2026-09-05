namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V13 replaces Chapter II's disconnected examples with one seven-step delivery level.
        // Existing players receive the clearer walkthrough once without losing campaign progress.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V13.Seen.";
    }
}
