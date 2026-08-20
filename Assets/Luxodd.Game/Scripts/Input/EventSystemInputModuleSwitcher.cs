using System;
using Object = UnityEngine.Object;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Luxodd.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EventSystem))]
    public class EventSystemInputModuleSwitcher : MonoBehaviour
    {
        private const string InputSystemUiInputModuleTypeFullName = "UnityEngine.InputSystem.UI.InputSystemUIInputModule";

        [Tooltip("If true, will auto-fix the input module in Editor via OnValidate.")]
        public bool autoFixInEditor = true;

#if UNITY_EDITOR
        private bool _isValidationScheduled;
#endif

        private void Awake()
        {
            EnsureCorrectModule();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!autoFixInEditor || _isValidationScheduled)
            {
                return;
            }

            _isValidationScheduled = true;
            UnityEditor.EditorApplication.delayCall += RunScheduledValidation;
        }

        private void RunScheduledValidation()
        {
            _isValidationScheduled = false;

            if (this == null || gameObject == null || !autoFixInEditor)
            {
                return;
            }

            EnsureCorrectModule();
        }
#endif

        private void EnsureCorrectModule()
        {
            Type inputSystemModuleType = GetInputSystemUiModuleType();
            if (inputSystemModuleType != null)
            {
                EnsureInputSystemModule(inputSystemModuleType);
                return;
            }

            EnsureStandaloneModule();
        }

        private void EnsureInputSystemModule(Type inputSystemModuleType)
        {
            Component inputSystemModule = GetComponent(inputSystemModuleType);
            if (inputSystemModule == null)
            {
                inputSystemModule = gameObject.AddComponent(inputSystemModuleType);
            }

            var standaloneInputModule = GetComponent<StandaloneInputModule>();
            if (standaloneInputModule != null)
            {
                DestroyImmediateSafe(standaloneInputModule);
            }
        }

        private void EnsureStandaloneModule()
        {
            var standaloneInputModule = GetComponent<StandaloneInputModule>();
            if (standaloneInputModule == null)
            {
                standaloneInputModule = gameObject.AddComponent<StandaloneInputModule>();
            }

            Component[] allModules = GetComponents<BaseInputModule>();
            for (int i = 0; i < allModules.Length; i++)
            {
                Component module = allModules[i];
                if (module == null || module == standaloneInputModule)
                {
                    continue;
                }

                DestroyImmediateSafe(module);
            }
        }

        private Type GetInputSystemUiModuleType()
        {
            Component[] allModules = GetComponents<BaseInputModule>();
            for (int i = 0; i < allModules.Length; i++)
            {
                Component module = allModules[i];
                if (module == null)
                {
                    continue;
                }

                Type moduleType = module.GetType();
                if (moduleType.FullName == InputSystemUiInputModuleTypeFullName)
                {
                    return moduleType;
                }
            }

            return Type.GetType($"{InputSystemUiInputModuleTypeFullName}, Unity.InputSystem");
        }

        private static void DestroyImmediateSafe(Component c)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) Object.DestroyImmediate(c);
            else Object.Destroy(c);
#else
            Object.Destroy(c);
#endif
        }
    }
}
