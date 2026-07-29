using UnityEngine;

namespace Parabox
{
    // Resources-backed reference to Luxodd's prefab. Keeping the vendor prefab in its original
    // package folder means plugin updates do not require copying or rebuilding it.
    public class LuxoddParaboxSettings : ScriptableObject
    {
        public GameObject pluginPrefab;
    }
}
