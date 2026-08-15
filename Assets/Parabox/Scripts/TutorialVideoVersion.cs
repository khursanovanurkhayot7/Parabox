namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V6 guarantees a gameplay mini-board at every chapter opener while retaining lessons for
        // mechanics introduced between openers. Players receive this corrected schedule once even
        // if an older presentation was already marked as seen.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V6.Seen.";
    }
}
