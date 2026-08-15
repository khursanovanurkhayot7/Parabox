namespace Parabox
{
    // PlayerPrefs migration lives separately so changing the walkthrough format creates a new
    // Unity asset and cannot be missed by an already-open Editor's incremental importer.
    static class TutorialVideoVersion
    {
        // V7 replaces straight mechanic diagrams with asymmetric, gameplay-style mini-puzzles and
        // distinct solution animations. Players receive the upgraded videos once even if the V6
        // chapter schedule was already marked as seen.
        public const string MechanicSeenPrefix = "Parabox.MechanicBriefing.V7.Seen.";
    }
}
