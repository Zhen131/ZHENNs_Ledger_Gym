using System.Collections.Generic;
using System.IO;
using Gym.Core;
using Gym.Runtime;
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
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Gym.EditorTools.GymSceneBuilder.BuildAll -quit
    /// </summary>
    public static class GymSceneBuilder
    {
        public const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";
        public const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";
        public const int TrainingAgentCount = 16;

        [MenuItem("Gym/Rebuild Prefab and Scenes")]
        public static void BuildAll()
        {
            GameObject prefab = BuildAgentPrefab();
            BuildTrainingScene(prefab);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Gym] prefab and scenes rebuilt");
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

        public static void SetBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(TrainingScenePath, true),
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
