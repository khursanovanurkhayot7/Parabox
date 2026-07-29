#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Parabox.Editor
{
    // Unity stores the Game-view speaker toggle as a global Editor preference.  This project was
    // being tested with that master mute enabled, which silences even correctly playing sources.
    // Clear it once when this project/session loads; if the user mutes again afterwards, respect it.
    [InitializeOnLoad]
    static class ParaboxAudioEditorGuard
    {
        const string AppliedKey = "Parabox.AudioEditorGuard.Applied";

        static ParaboxAudioEditorGuard()
        {
            EditorApplication.delayCall += ApplyOnce;
        }

        static void ApplyOnce()
        {
            if (SessionState.GetBool(AppliedKey, false)) return;
            SessionState.SetBool(AppliedKey, true);

            if (!EditorUtility.audioMasterMute) return;
            EditorUtility.audioMasterMute = false;
            Debug.Log("Parabox: Unity Game-view audio was muted; audio has been enabled for testing.");
        }
    }
}
#endif
