using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>
    /// The demo build: the same game with the <c>HEADLOSS_DEMO</c> define, which applies <c>balancing.demo</c> (highest tier,
    /// no new cities). No other code differs. Writes to <c>Builds/demo/</c>.
    /// </summary>
    public static class ReleaseBuilds
    {
        public const string DemoDefine = "HEADLOSS_DEMO";
        public const string DemoPath = "Builds/demo/HeadLoss.exe";

        /// <summary>Starts the build on the next Editor update, so a CLI call returns at once. The result goes to the console.</summary>
        [MenuItem("Head Loss/Build Windows Demo")]
        public static void BuildDemo() => EditorApplication.delayCall += RunDemoBuild;

        static void RunDemoBuild()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = DemoPath,
                target = BuildTarget.StandaloneWindows64,
                extraScriptingDefines = new[] { DemoDefine },
            });
            BuildSummary sum = report.summary;
            string line = $"Head Loss demo build: {sum.result}, {sum.totalErrors} errors, {sum.totalSize / (1024 * 1024)} MB at {DemoPath}";
            if (sum.result == BuildResult.Succeeded) Debug.Log(line);
            else Debug.LogError(line);
        }
    }
}
