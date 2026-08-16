namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V12 presents the bundled solver replay at 0.7x speed. Existing players receive each
        // easier-to-follow chapter video once without losing campaign progress.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V12.Seen.";
    }
}
