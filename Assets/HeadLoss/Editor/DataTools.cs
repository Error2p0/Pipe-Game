using System.Collections.Generic;
using System.IO;
using HeadLoss.Core.Data;
using HeadLoss.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>
    /// Keeps the data catalog in sync with the JSON files and validates them. The catalog is rebuilt after
    /// imports, and again whenever it is stale on entering Play mode or before a player build (a domain
    /// reload can drop the post-import rebuild).
    /// </summary>
    [InitializeOnLoad]
    public sealed class DataTools : AssetPostprocessor, UnityEditor.Build.IPreprocessBuildWithReport
    {
        static DataTools()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode && CatalogIsStale()) RebuildCatalog();
            };
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            if (CatalogIsStale()) RebuildCatalog();
        }

        /// <summary>True when the catalog misses a JSON file, lists a removed one, or holds an unloaded reference.</summary>
        public static bool CatalogIsStale()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DataCatalog>(CatalogPath);
            if (catalog == null) return true;
            var files = new HashSet<string>();
            foreach (string f in Directory.GetFiles(DataFolder, "*.json", SearchOption.AllDirectories))
                files.Add(f.Replace('\\', '/').Substring(DataFolder.Length + 1));
            if (files.Count != catalog.entries.Count) return true;
            foreach (DataCatalog.Entry e in catalog.entries)
                if (e.text == null || !files.Contains(e.path)) return true;
            return false;
        }

        public const string DataFolder = "Assets/HeadLoss/Data";
        public const string CatalogPath = "Assets/HeadLoss/Data/_Catalog.asset";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string[] list in new[] { imported, deleted, moved, movedFrom })
                foreach (string p in list)
                    if (p.StartsWith(DataFolder) && p.EndsWith(".json"))
                    {
                        EditorApplication.delayCall += () => RebuildCatalog();
                        return;
                    }
        }

        [MenuItem("Head Loss/Rebuild Data Catalog")]
        public static DataCatalog RebuildCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DataCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DataCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var entries = new List<DataCatalog.Entry>();
            var files = new List<string>(Directory.GetFiles(DataFolder, "*.json", SearchOption.AllDirectories));
            files.Sort(System.StringComparer.Ordinal);
            foreach (string file in files)
            {
                string assetPath = file.Replace('\\', '/');
                string rel = assetPath.Substring(DataFolder.Length + 1);
                entries.Add(new DataCatalog.Entry { path = rel, text = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath) });
            }
            catalog.entries = entries;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            return catalog;
        }

        [MenuItem("Head Loss/Validate Data")]
        public static void ValidateMenu()
        {
            int count = Validate();
            if (count == 0) Debug.Log("Head Loss data: all files valid.");
        }

        /// <summary>Validates the data folder; logs each problem. Returns the number of errors.</summary>
        public static int Validate()
        {
            try
            {
                DataRepository data = DataRepository.Load(new FileDataSource(DataFolder));
                List<string> errors = DataValidator.Validate(data);
                foreach (string e in errors) Debug.LogError("Data: " + e);
                return errors.Count;
            }
            catch (DataLoadException e)
            {
                Debug.LogError("Data: " + e.Message);
                return 1;
            }
        }
    }
}
