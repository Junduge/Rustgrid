using System;
using System.IO;
using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Agents;
using Rustgrid.Tactics.Composition;
using Rustgrid.Tactics.Core;
using Rustgrid.Tactics.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rustgrid.Tactics.Authoring
{
    /// <summary>
    /// Builds the battle scene through editor APIs (scenes are never edited as text) and runs a
    /// headless smoke battle whose hash must equal the dotnet runner's for the same inputs.
    /// Batch: Unity -batchmode -nographics -projectPath games/tactics/unity
    ///        -executeMethod Rustgrid.Tactics.Authoring.ProjectSetup.BatchSetupAndSmoke -expectedHash HEX
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Battle.unity";
        public const string PlaceholderMaterialPath = "Assets/_Project/Art/Placeholder.mat";

        [MenuItem("Rustgrid/Rebuild Battle Scene")]
        public static void CreateBattleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            camera.orthographic = true;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var battle = new GameObject("Battle");
            var view = battle.AddComponent<BattleView>();
            var input = battle.AddComponent<BattleInput>();
            var hud = battle.AddComponent<BattleHud>();
            var bootstrap = battle.AddComponent<BattleBootstrap>();
            Wire(view, "viewCamera", camera);
            Wire(view, "baseMaterial", PlaceholderMaterial());
            Wire(input, "view", view);
            Wire(bootstrap, "view", view);
            Wire(bootstrap, "input", input);
            Wire(bootstrap, "hud", hud);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Rustgrid";
            PlayerSettings.companyName = "Rustgrid";
            AssetDatabase.SaveAssets();
            Debug.Log("Rustgrid: battle scene saved to " + ScenePath);
        }

        [MenuItem("Rustgrid/Run Smoke Battle")]
        public static string RunSmokeBattle()
        {
            var data = new GameDataSource(GameDataSource.EditorRoot);
            var rules = TacticsData.ReadRules(data.ReadRules());
            var level = TacticsData.ReadLevel(data.ReadLevel("arena_01"));
            var summary = Episode.Play(level, rules, 42, new IAgent[] { new GreedyAgent(), new GreedyAgent() });
            string hash = StableHasher.ToHex(summary.FinalHash);
            Debug.Log($"SMOKE level=arena_01 seed=42 sides=greedy,greedy outcome={summary.Outcome} winner={summary.WinnerSide} " +
                      $"turns={summary.Turns} ticks={summary.Ticks} final_hash={hash}");
            return hash;
        }

        public static void BatchSetupAndSmoke()
        {
            int code = 0;
            try
            {
                CreateBattleScene();
                string hash = RunSmokeBattle();
                string expected = Argument("-expectedHash");
                if (expected != null && expected != hash)
                {
                    Debug.LogError($"SMOKE MISMATCH: Unity {hash}, dotnet {expected}");
                    code = 2;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>A plain URP Lit material; referenced from the scene so the build keeps its shader.</summary>
        private static Material PlaceholderMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
            if (existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP Lit shader not found");
            Directory.CreateDirectory(Path.GetDirectoryName(PlaceholderMaterialPath)!);
            var material = new Material(shader) { name = "Placeholder" };
            material.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(material, PlaceholderMaterialPath);
            return material;
        }

        private static void Wire(UnityEngine.Object component, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException($"{component.GetType().Name}.{field} not found");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
