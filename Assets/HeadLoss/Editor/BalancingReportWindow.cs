using System.Text;
using HeadLoss.Core.Data;
using HeadLoss.Game.Balancing;
using UnityEditor;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>Editor window <c>Head Loss/Balancing Report</c> (<c>13-balancing.md</c>): tune with it, not by feel.</summary>
    public sealed class BalancingReportWindow : EditorWindow
    {
        string _text = "Press Run to build every reference layout and check the balancing targets.";
        Vector2 _scroll;
        int _offTarget;

        [MenuItem("Head Loss/Balancing Report")]
        public static void Open() => GetWindow<BalancingReportWindow>("Balancing Report").Show();

        /// <summary>Runs the report on the data folder (also callable from the CLI) and returns its text.</summary>
        public static string RunReport(out int offTarget)
        {
            DataRepository data = DataRepository.Load(new FileDataSource(DataTools.DataFolder));
            BalancingReport.Result r = BalancingReport.Run(data);
            offTarget = r.Checks.FindAll(c => !c.Pass).Count;
            var sb = new StringBuilder();
            foreach (string line in r.Lines) sb.AppendLine(line);
            sb.AppendLine($"{r.Checks.Count - offTarget} of {r.Checks.Count} checks on target.");
            return sb.ToString();
        }

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run", GUILayout.Width(120))) _text = RunReport(out _offTarget);
            if (GUILayout.Button("Copy", GUILayout.Width(120))) EditorGUIUtility.systemCopyBuffer = _text;
            GUILayout.FlexibleSpace();
            if (_offTarget > 0) EditorGUILayout.LabelField($"{_offTarget} off target", EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_text, new GUIStyle(EditorStyles.textArea) { wordWrap = true, richText = false });
            EditorGUILayout.EndScrollView();
        }
    }
}
