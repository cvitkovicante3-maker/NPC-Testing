#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Removes "missing script" components (left over from the HDRP version) from every prefab in the Day & Night Cycle folder.
// Put this file in a folder named "Editor", then use the menu  Tools > Remove Missing Scripts (Day & Night Cycle).
public static class RemoveMissingScripts
{
    private const string Folder = "Assets/Day & Night Cycle";

    [MenuItem("Tools/Remove Missing Scripts (Day & Night Cycle)")]
    public static void Run()
    {
        int removed = 0, prefabs = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int count = 0;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                count += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            if (count > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                removed += count;
                prefabs++;
                Debug.Log($"Removed {count} missing script(s) from {path}");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Done: removed {removed} missing script(s) from {prefabs} prefab(s).");
    }
}
#endif
