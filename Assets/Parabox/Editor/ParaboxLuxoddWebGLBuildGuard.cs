#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Parabox.EditorTools
{
    // Prevents a manual Build Profile change from silently producing Unity's 960x600 default
    // page again. That page misaligns the artwork and UI hit targets inside the Luxodd iframe and
    // exposes Unity warning logs as yellow banners over the game.
    public sealed class ParaboxLuxoddWebGLBuildGuard : IPreprocessBuildWithReport
    {
        const string TemplateName = "PROJECT:LuxoddTemplate";
        const string TemplatePath = "Assets/WebGLTemplates/LuxoddTemplate/index.html";

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            ParaboxPrebuiltUiGenerator.ValidateSilent();
            ApplyRequiredSettings(true);
        }

        [MenuItem("Tools/Parabox/Apply Luxodd WebGL Build Settings")]
        public static void ApplyFromMenu()
        {
            ApplyRequiredSettings(true);
            Debug.Log("Parabox Luxodd WebGL settings applied: custom full-screen template, " +
                      "uncompressed files and browser caching disabled.");
        }

        [MenuItem("Tools/Parabox/Build Luxodd WebGL Upload")]
        public static void BuildUploadReady()
        {
            ApplyRequiredSettings(true);

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path)) scenes.Add(scene.path);
            if (scenes.Count == 0)
                throw new BuildFailedException("No enabled scenes are configured for the WebGL build.");

            string folderName = "LuxoddWebGLUpload-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string output = Path.GetFullPath(folderName);
            Directory.CreateDirectory(output);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Luxodd WebGL build failed: " + report.summary.result);

            Debug.Log("LUXODD_WEBGL_OUTPUT=" + output);
        }

        static void ApplyRequiredSettings(bool failWhenMissing)
        {
            if (!File.Exists(TemplatePath))
            {
                string message = "Luxodd WebGL template is missing: " + TemplatePath;
                if (failWhenMissing) throw new BuildFailedException(message);
                Debug.LogError(message);
                return;
            }

            PlayerSettings.WebGL.template = TemplateName;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;

            // Most of the approved menu/map is world-space artwork, while its clickable
            // hotspots, labels and countdown are serialized uGUI Images/Text generated in edit mode.
            // Shader stripping therefore cannot always see the UI/Default dependency and
            // WebGL renders those controls as solid magenta rectangles. Pin the built-in
            // uGUI shaders into every upload build so the visible faces and their hit targets
            // use the same material in the browser as they do in the Editor.
            EnsureAlwaysIncludedShader("UI/Default", failWhenMissing);
            EnsureAlwaysIncludedShader("UI/DefaultETC1", false);
        }

        static void EnsureAlwaysIncludedShader(string shaderName, bool failWhenMissing)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                if (failWhenMissing)
                    throw new BuildFailedException("Required WebGL shader was not found: " + shaderName);
                Debug.LogWarning("Optional WebGL shader was not found: " + shaderName);
                return;
            }

            Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settingsAssets == null || settingsAssets.Length == 0)
                throw new BuildFailedException("Could not load ProjectSettings/GraphicsSettings.asset.");

            var serializedSettings = new SerializedObject(settingsAssets[0]);
            SerializedProperty included = serializedSettings.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
                throw new BuildFailedException("GraphicsSettings has no m_AlwaysIncludedShaders property.");

            for (int i = 0; i < included.arraySize; i++)
                if (included.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    return;

            int newIndex = included.arraySize;
            included.InsertArrayElementAtIndex(newIndex);
            included.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
