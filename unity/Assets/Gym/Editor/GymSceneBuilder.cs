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

namespace Gym.Editor
{
    /// <summary>
    /// 用代码生成 Agent 的 prefab 和各个 scene，这样不用打开 editor 界面，从命令行就能重新生成：
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.GymSceneBuilder.BuildAll -quit
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

        /// <summary>只生成 Eval scene 和打包用的 scene 列表；prefab 和其他 scene 保持原样。</summary>
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
        /// 一个只跑一个评估 episode 的 Agent，加上推着它走 step、并写评估流水的 EvalRunner。
        /// 这里 Behavior Type 保持 Default（没有模型，所以它一直不动）；
        /// BuildScript.BuildMacEval 会复制一份，挂上模型并设为 Inference Only。
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

            AssetFolders.Ensure(Path.GetDirectoryName(EvalScenePath));
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

                AssetFolders.Ensure(Path.GetDirectoryName(PrefabPath));
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
            AssetFolders.Ensure(Path.GetDirectoryName(TrainingScenePath));
            EditorSceneManager.SaveScene(scene, TrainingScenePath);
        }

        public static Material BuildChartMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ChartMaterialPath);
            Shader shader = Shader.Find("Sprites/Default");
            if (material == null)
            {
                AssetFolders.Ensure(Path.GetDirectoryName(ChartMaterialPath));
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
            AddChartCamera();

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

            ScreenViews views = AddScreenViews(agent, chartMaterial);
            views.Hud.Controller = controller;

            AssetFolders.Ensure(Path.GetDirectoryName(PlayScenePath));
            EditorSceneManager.SaveScene(scene, PlayScenePath);
        }

        /// <summary>
        /// 给人看的 scene 共用的一套画面：语言切换、读数面板、K 线图、口袋、限帧、站在最新那根 candle 上的小人。
        /// 只有这类 scene 限帧；Training、Eval scene 不挂这些。
        /// </summary>
        static ScreenViews AddScreenViews(TradingAgent agent, Material chartMaterial)
        {
            var language = new GameObject("Language").AddComponent<PlayLanguageSwitch>();

            var hud = new GameObject("HUD").AddComponent<HudView>();
            hud.Agent = agent;
            hud.Language = language;

            CandleChartView chart = AddCandleChart(agent, chartMaterial);

            var wallet = new GameObject("Wallet").AddComponent<WalletView>();
            wallet.Agent = agent;
            wallet.Language = language;

            new GameObject("FrameRateLimiter").AddComponent<FrameRateLimiter>();

            var avatar = new GameObject("Avatar").AddComponent<AvatarView>();
            avatar.Agent = agent;
            avatar.Chart = chart;
            avatar.Language = language;

            return new ScreenViews(language, hud, chart, wallet, avatar);
        }

        /// <summary><see cref="AddScreenViews"/> 建出来的那几个画面组件，给调用方接到各自的控制器上。</summary>
        readonly struct ScreenViews
        {
            public ScreenViews(PlayLanguageSwitch language, HudView hud, CandleChartView chart, WalletView wallet, AvatarView avatar)
            {
                Language = language;
                Hud = hud;
                Chart = chart;
                Wallet = wallet;
                Avatar = avatar;
            }

            public readonly PlayLanguageSwitch Language;
            public readonly HudView Hud;
            public readonly CandleChartView Chart;
            public readonly WalletView Wallet;
            public readonly AvatarView Avatar;
        }

        /// <summary>一台对着图表、深色背景的正交相机。</summary>
        static void AddChartCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            camera.transform.position = new Vector3(0, 0, -10);
        }

        /// <summary>图表放在 <see cref="PlayLayout.Chart"/> 那个框里，给左边的面板和右边、下边的刻度让地方。</summary>
        static CandleChartView AddCandleChart(TradingAgent agent, Material chartMaterial)
        {
            Rect area = PlayLayout.Chart;
            var chartObject = new GameObject("CandleChart");
            chartObject.transform.position = new Vector3(area.center.x, area.center.y, 0);
            chartObject.AddComponent<MeshFilter>();
            chartObject.AddComponent<MeshRenderer>().sharedMaterial = chartMaterial;
            var chart = chartObject.AddComponent<CandleChartView>();
            chart.Agent = agent;
            chart.Width = area.width;
            chart.Height = area.height;
            return chart;
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
    }
}
