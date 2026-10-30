using System.IO;
using HeadLoss.Core.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>
    /// Playtest builds started outside Steam need <c>steam_appid.txt</c> next to the executable (with the app id from
    /// <c>game.json → steam.appId</c>). Store builds uploaded to Steam do not ship it.
    /// </summary>
    public sealed class SteamBuildStep : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10;

        public void OnPostprocessBuild(BuildReport report)
        {
            BuildTarget t = report.summary.platform;
            if (t != BuildTarget.StandaloneWindows64 && t != BuildTarget.StandaloneWindows && t != BuildTarget.StandaloneLinux64 && t != BuildTarget.StandaloneOSX) return;
            DataRepository data = DataRepository.Load(new FileDataSource(DataTools.DataFolder));
            string folder = Path.GetDirectoryName(report.summary.outputPath) ?? ".";
            File.WriteAllText(Path.Combine(folder, "steam_appid.txt"), data.Game.Steam.AppId.ToString());
            Debug.Log($"Head Loss: steam_appid.txt ({data.Game.Steam.AppId}) written next to the build.");
        }
    }
}
