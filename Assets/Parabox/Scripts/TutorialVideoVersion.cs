namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V8 moves the room-box lesson to Chapter III and demonstrates it before the new recursive
        // campaign begins. Players receive the corrected video once even if V7 was already seen.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V8.Seen.";
    }
}
