namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V5 replaces the abstract rule diagram with spoiler-free, prebuilt gameplay mini-boards.
        // Players who saw the earlier card should receive the corrected visual lesson once.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V5.Seen.";
    }
}
