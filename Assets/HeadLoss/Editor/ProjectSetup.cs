using System.Collections.Generic;
using System.IO;
using HeadLoss.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace HeadLoss.Editor
{
    /// <summary>Creates the UI assets and the Boot/Main scenes with their references wired (reproducible setup).</summary>
    public static class ProjectSetup
    {
        const string ThemePath = "Assets/HeadLoss/UI/HeadLossTheme.tss";
        const string PanelPath = "Assets/HeadLoss/UI/HeadLossPanelSettings.asset";
        const string StylePath = "Assets/HeadLoss/UI/HeadLoss.uss";
        const string ShaderPath = "Assets/HeadLoss/Shaders/VertexColorUnlit.shader";
        const string BootScene = "Assets/HeadLoss/Scenes/Boot.unity";
        const string MainScene = "Assets/HeadLoss/Scenes/Main.unity";

        [MenuItem("Head Loss/Setup/Create Scenes")]
        public static void CreateScenes()
        {
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);

            DataTools.RebuildCatalog();
            AssetDatabase.SaveAssets();

            Scene boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Boot").AddComponent<Boot>();
            EditorSceneManager.SaveScene(boot, BootScene);

            Scene main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GameRoot").AddComponent<GameRoot>();
            var so = new SerializedObject(root);
            // Reload from disk: objects created above are unloaded when a new scene opens.
            so.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HeadLoss.Unity.Data.DataCatalog>(DataTools.CatalogPath);
            so.FindProperty("panelSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            so.FindProperty("styleSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            so.FindProperty("unlitShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(main, MainScene);

            EditorBuildSettings.scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootScene, true),
                new EditorBuildSettingsScene(MainScene, true),
            }.ToArray();
            Debug.Log("Head Loss: scenes created (Boot, Main) and added to the build.");
        }
    }
}
