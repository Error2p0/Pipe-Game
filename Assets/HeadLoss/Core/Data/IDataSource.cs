using System.Collections.Generic;
using System.IO;

namespace HeadLoss.Core.Data
{
    /// <summary>Supplies the text of data files by path relative to the data folder (forward slashes).</summary>
    public interface IDataSource
    {
        /// <summary>All available file paths, relative, forward slashes, sorted ordinally.</summary>
        IReadOnlyList<string> ListFiles();

        /// <summary>Text of a file, or null if it does not exist.</summary>
        string? ReadText(string relativePath);
    }

    /// <summary>Reads data files from a folder on disk (tests, tools, the editor validator).</summary>
    public sealed class FileDataSource : IDataSource
    {
        readonly string _root;

        public FileDataSource(string rootFolder)
        {
            _root = Path.GetFullPath(rootFolder);
        }

        public IReadOnlyList<string> ListFiles()
        {
            var list = new List<string>();
            if (!Directory.Exists(_root)) return list;
            foreach (string f in Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories))
            {
                list.Add(Path.GetRelativePath(_root, f).Replace('\\', '/'));
            }
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        public string? ReadText(string relativePath)
        {
            string path = Path.Combine(_root, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    /// <summary>In-memory data source (the Unity TextAsset adapter and tests).</summary>
    public sealed class MemoryDataSource : IDataSource
    {
        readonly SortedDictionary<string, string> _files = new SortedDictionary<string, string>(System.StringComparer.Ordinal);

        public void Add(string relativePath, string text) => _files[relativePath.Replace('\\', '/')] = text;

        public IReadOnlyList<string> ListFiles() => new List<string>(_files.Keys);

        public string? ReadText(string relativePath) =>
            _files.TryGetValue(relativePath, out string text) ? text : null;
    }
}
