using System.Collections.Generic;
using System.IO;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using Gym.Runtime.Evaluation;
using Gym.Runtime.Play;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gym.EditorTools
{
    /// <summary>
    /// Builds the agent prefab and the scenes from code, so they can be regenerated
    /// from the command line without opening the editor UI:
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.EditorTools.GymSceneBuilder.BuildAll -quit
    /// </summary>
    public static class GymSceneBuilder
    {
        public const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";
        public const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";
        public const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";
        public const string EvalScenePath = "Assets/Gym/Scenes/Eval.unity";
        public const string ChartMaterialPath = "Assets/Gym/Materials/CandleChart.mat";
        public const int TrainingAgentCount = 16;

        [MenuItem("Gym/Rebuild Prefab and Scenes")]
        public static void BuildAll()
        {
            GameObject prefab = BuildAgentPrefab();
            BuildTrainingScene(prefab);
            BuildPlayScene(prefab, BuildChartMaterial());
            BuildEvalScene(prefab);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Gym] prefab and scenes rebuilt");
        }

        /// <summary>Only the Eval scene and the build list; leaves the prefab and the other scenes alone.</summary>
        public static void BuildEvalOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = BuildAgentPrefab();
            BuildEvalScene(prefab);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Gym] Eval scene rebuilt");
        }

        /// <summary>
        /// One agent that runs a single evaluation episode, and the EvalRunner that steps it
        /// and writes the log. Behavior Type stays Default here (no model, so it holds);
        /// BuildScript.BuildMacEval makes a copy with the model and Inference Only.
        /// </summary>
        public static void BuildEvalScene(GameObject prefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var agentObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            agentObject.name = "TradingAgent (Eval)";
            var agent = agentObject.GetComponent<TradingAgent>();
            agent.StartMode = AgentStartMode.Evaluation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(agent);

            var runner = new GameObject("EvalRunner").AddComponent<EvalRunner>();
            runner.Agent = agent;

            EnsureFolder(Path.GetDirectoryName(EvalScenePath));
            EditorSceneManager.SaveScene(scene, EvalScenePath);
        }

        public static GameObject BuildAgentPrefab()
        {
            var go = new GameObject("TradingAgent");
            try
            {
                var agent = go.AddComponent<TradingAgent>();
                agent.MaxStep = 0;

                var behavior = go.GetComponent<BehaviorParameters>();
                behavior.BehaviorName = TradingAgent.BehaviorNameValue;
                behavior.BehaviorType = BehaviorType.Default;
                behavior.BrainParameters.VectorObservationSize = ObservationBuilder.Size;
                behavior.BrainParameters.NumStackedVectorObservations = 1;
                behavior.BrainParameters.ActionSpec = new ActionSpec(ActionCodec.ContinuousSize, new[] { ActionCodec.BranchSize });

                var requester = go.AddComponent<DecisionRequester>();
                requester.DecisionPeriod = 1;
                requester.DecisionStep = 0;
                requester.TakeActionsBetweenDecisions = false;

                EnsureFolder(Path.GetDirectoryName(PrefabPath));
                return PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        public static void BuildTrainingScene(GameObject prefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < TrainingAgentCount; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = $"TradingAgent ({i:00})";
                var agent = instance.GetComponent<TradingAgent>();
                agent.AgentIndex = i;
                PrefabUtility.RecordPrefabInstancePropertyModifications(agent);
            }
            EnsureFolder(Path.GetDirectoryName(TrainingScenePath));
            EditorSceneManager.SaveScene(scene, TrainingScenePath);
        }

        public static Material BuildChartMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ChartMaterialPath);
            Shader shader = Shader.Find("Sprites/Default");
            if (material == null)
            {
                EnsureFolder(Path.GetDirectoryName(ChartMaterialPath));
                material = new Material(shader) { name = "CandleChart" };
                AssetDatabase.CreateAsset(material, ChartMaterialPath);
            }
            else
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        public static void BuildPlayScene(GameObject prefab, Material chartMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            camera.transform.position = new Vector3(0, 0, -10);

            var agentObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            agentObject.name = "TradingAgent (Play)";
            var agent = agentObject.GetComponent<TradingAgent>();
            agent.StartMode = AgentStartMode.PlayFromConfig;
            PrefabUtility.RecordPrefabInstancePropertyModifications(agent);
            var behavior = agentObject.GetComponent<BehaviorParameters>();
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            PrefabUtility.RecordPrefabInstancePropertyModifications(behavior);

            var controller = new GameObject("PlayController").AddComponent<PlayController>();
            controller.Agent = agent;

            var hud = new GameObject("HUD").AddComponent<HudView>();
            hud.Agent = agent;
            hud.Controller = controller;

            var chartObject = new GameObject("CandleChart");
            chartObject.transform.position = new Vector3(0, -1.4f, 0);
            chartObject.AddComponent<MeshFilter>();
            chartObject.AddComponent<MeshRenderer>().sharedMaterial = chartMaterial;
            chartObject.AddComponent<CandleChartView>().Agent = agent;

            EnsureFolder(Path.GetDirectoryName(PlayScenePath));
            EditorSceneManager.SaveScene(scene, PlayScenePath);
        }

        public static void SetBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(TrainingScenePath, true),
                new EditorBuildSettingsScene(PlayScenePath, true),
                new EditorBuildSettingsScene(EvalScenePath, true),
            };
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
