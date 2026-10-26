using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using UnityEngine;

namespace HeadLoss.Unity.Data
{
    /// <summary>
    /// References every JSON file under <c>Assets/HeadLoss/Data</c> as TextAssets so the player can load
    /// them. Rebuilt automatically by the editor (<c>DataCatalogBuilder</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "Head Loss/Data Catalog")]
    public sealed class DataCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            /// <summary>Path relative to the data folder, forward slashes.</summary>
            public string path;
            public TextAsset text;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>Loads all data through the shared parser.</summary>
        public DataRepository Load()
        {
            var source = new MemoryDataSource();
            foreach (Entry e in entries)
                if (e.text != null) source.Add(e.path, e.text.text);
            return DataRepository.Load(source);
        }
    }
}
