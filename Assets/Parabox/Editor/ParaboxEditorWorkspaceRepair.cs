using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    // Repairs only the Unity EDITOR workspace. It never changes a scene, prefab, camera or build.
    // Automatic execution requires a one-shot request under Library, so other developers keep
    // their preferred layouts. The menu item remains available if the editor layout breaks again.
    public static class ParaboxEditorWorkspaceRepair
    {
        const string RequestName = "ParaboxRepairEditorLayout.request";
        static double nextRequestPoll;

        [InitializeOnLoadMethod]
        static void QueueRequestedRepair()
        {
            EditorApplication.update -= PollRepairRequest;
            EditorApplication.update += PollRepairRequest;
            PollRepairRequest();
        }

        static void PollRepairRequest()
        {
            if (EditorApplication.timeSinceStartup < nextRequestPoll) return;
            nextRequestPoll = EditorApplication.timeSinceStartup + 0.5d;
            if (File.Exists(RequestPath())) TryRequestedRepair();
        }

        [MenuItem("Tools/Parabox/Repair Unity Editor Workspace")]
        public static void RepairFromMenu()
        {
            RepairWorkspace();
        }

        static void TryRequestedRepair()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string request = RequestPath();
            if (!File.Exists(request)) return;
            File.Delete(request);
            RepairWorkspace();
        }

        static void RepairWorkspace()
        {
            bool restored = EditorApplication.ExecuteMenuItem("Window/Layouts/Default");

            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType != null)
            {
                EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, "Game", true);
                gameView.maximized = false;
                gameView.Focus();
                gameView.Repaint();
            }

            Debug.Log(restored
                ? "Parabox: Unity editor layout restored to Default and Game view focused."
                : "Parabox: Game view focused. If panels still overlap, choose Layout > Default.");
        }

        static string RequestPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Library", RequestName);
        }
    }
}
