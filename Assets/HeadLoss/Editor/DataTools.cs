using System.Collections.Generic;
using System.IO;
using HeadLoss.Core.Data;
using HeadLoss.Unity.Data;
using UnityEditor;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>Keeps the data catalog in sync with the JSON files and validates them.</summary>
    public sealed class DataTools : AssetPostprocessor
    {
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
