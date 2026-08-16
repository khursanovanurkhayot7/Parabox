namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V10 includes the expanded Chapter II mirror slalom. Players who saw the shorter V9
        // demonstration receive the improved tutorial once without resetting campaign progress.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V10.Seen.";
    }
}
