using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GradientLander.Editor
{
    public static class ProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/GradientLander.unity";

        [MenuItem("Gradient Lander/Rebuild Main Scene")]
        public static void CreateProject()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("Gradient Lander");
            root.AddComponent<LunarLanderController>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Gradient Learning Lab";
            PlayerSettings.productName = "Gradient Lander";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Gradient Lander scene and build settings created successfully.");
        }

        [MenuItem("Gradient Lander/Validate Model")]
        public static void ValidateModel()
        {
            MissionLayout layout = LanderMath.GenerateLayout(LanderMath.DemonstrationSeed);
            MissionLayout repeated = LanderMath.GenerateLayout(LanderMath.DemonstrationSeed);
            MissionLayout different = LanderMath.GenerateLayout(LanderMath.DemonstrationSeed + 1);
            AssertNear(layout.TargetX, repeated.TargetX, "identical seeds should reproduce the target");
            AssertNear(layout.Platforms[2][1].X, repeated.Platforms[2][1].X, "identical seeds should reproduce platforms");
            if (Mathf.Approximately(layout.TargetX, different.TargetX))
                throw new InvalidOperationException("Different mission seeds should randomize the target position.");
            if (layout.TargetX < .17f || layout.TargetX > .83f)
                throw new InvalidOperationException("Random ground target must stay inside the safe ground range.");

            float error = layout.TargetX - LanderMath.StartX;
            ThrustSolution centeredThrust = LanderMath.CalculateThrust(1f, 0f, LanderMath.StartX, layout.TargetX);
            AssertNear(centeredThrust.EndX, layout.TargetX, "w = 1 should reach the randomized guidance target");
            AssertNear(centeredThrust.LeftPower, error, "left command");
            AssertNear(centeredThrust.RightPower, -error, "right command");
            LanderRunResult centered = LanderMath.ScoreGroundLanding(centeredThrust, layout);
            AssertNear(centered.Distance, 0f, "ground target landing distance");
            AssertNear(centered.Cost, .2f * error * error, "target landing cost");

            ThrustSolution neutralThrust = LanderMath.CalculateThrust(0f, .5f, LanderMath.StartX, layout.TargetX);
            LanderRunResult neutral = LanderMath.ScoreGroundLanding(neutralThrust, layout);
            AssertNear(neutral.LandingX, LanderMath.StartX, "w = 0 should not move horizontally");
            AssertNear(neutral.TotalPower, 1f, "total power should equal 2b");
            AssertNear(neutral.Cost, error * error + .05f, "neutral landing cost");

            float x = LanderMath.StartX;
            float worldDepth = 0f;
            float cumulativeCost = 0f;
            int skips = 0;
            int platformLandings = 0;
            int guard = 0;
            LanderRunResult final = default;
            while (guard++ < 20)
            {
                ThrustSolution thrust = LanderMath.CalculateThrust(.8f, .55f, x, layout.TargetX);
                if (LanderMath.TryFindNextPlatformCollision(layout, worldDepth, x, thrust.EndX,
                    LanderMath.FrameCount, out PlatformCollision collision))
                {
                    if (platformLandings == 0) skips = collision.Platform.FrameIndex;
                    LanderRunResult platformResult = LanderMath.ScorePlatformLanding(thrust, collision);
                    cumulativeCost += platformResult.Cost;
                    x = collision.ShipX;
                    worldDepth = collision.WorldDepth;
                    platformLandings++;
                    continue;
                }

                x = thrust.EndX;
                worldDepth = LanderMath.FrameCount;
                final = LanderMath.ScoreGroundLanding(thrust, layout);
                cumulativeCost += final.Cost;
                break;
            }
            if (!final.HitFinalTarget)
                throw new InvalidOperationException("Default parameters should complete the final target landing.");
            if (platformLandings < 1 || skips < 1)
                throw new InvalidOperationException("Default route must demonstrate both an optional platform landing and a skipped frame.");
            if (cumulativeCost <= 0f || cumulativeCost >= LanderMath.MissionBudget)
                throw new InvalidOperationException("Cumulative mission cost must deduct a positive amount without exhausting the budget.");

            MissionLayout graph = LanderMath.GenerateGraphChallengeLayout();
            if (graph.Mode != MissionMode.GraphChallenge ||
                graph.TotalDepth != LanderMath.ChallengeDepth ||
                graph.CorrectPath.Length != LanderMath.GraphLandingCount + 2)
                throw new InvalidOperationException("Graph challenge must contain the extended calculated route.");
            if (LanderMath.GraphObjective(-1.55f) > -6f ||
                LanderMath.GraphObjective(-.7f) < -2.8f ||
                !Mathf.Approximately(LanderMath.GraphObjective(0f), -3f))
                throw new InvalidOperationException("Fitted quartic must retain the supplied deep and shallow valleys.");

            int checkpointCount = 0;
            int broadPlatformCount = 0;
            int correctRoutePlatformCount = 0;
            int decoyCount = 0;
            int[] correctStopsPerStep = new int[LanderMath.GraphLandingCount];
            for (int section = 0; section < graph.Platforms.Length; section++)
            {
                for (int platformIndex = 0; platformIndex < graph.Platforms[section].Length; platformIndex++)
                {
                    PlatformDefinition platform = graph.Platforms[section][platformIndex];
                    if (platform.IsCheckpoint) checkpointCount++;
                    if (platform.HalfWidth >= .49f) broadPlatformCount++;
                    if (platform.IsCorrectRoute)
                    {
                        correctRoutePlatformCount++;
                        if (platform.RouteStep < 0 || platform.RouteStep >= correctStopsPerStep.Length)
                            throw new InvalidOperationException("Correct route platform has an invalid route-step index.");
                        correctStopsPerStep[platform.RouteStep]++;
                    }
                    else decoyCount++;
                }
            }
            if (checkpointCount != LanderMath.CheckpointCount || broadPlatformCount != LanderMath.CheckpointCount)
                throw new InvalidOperationException("Exactly three—and only three—platforms may span the screen.");
            int expectedDecoys = 3 + LanderMath.GraphAmbientDecoyCount;
            if (correctRoutePlatformCount != LanderMath.GraphLandingCount || decoyCount != expectedDecoys)
                throw new InvalidOperationException("Extended challenge must contain one complete route plus the intended sparse decoy platforms.");
            for (int step = 0; step < correctStopsPerStep.Length; step++)
                if (correctStopsPerStep[step] != 1)
                    throw new InvalidOperationException($"Gradient step {step + 1} must have exactly one correct platform.");
            if (Mathf.Abs(graph.CorrectPath[0].x - graph.TargetX) < .20f)
                throw new InvalidOperationException("Correct route must visibly curve across the map rather than drop vertically.");

            float graphX = graph.StartX;
            float graphDepth = 0f;
            int collectedCheckpoints = 0;
            for (int step = 0; step < LanderMath.GraphLandingCount; step++)
            {
                float guidanceX = LanderMath.GetGuidanceTarget(graph, graphDepth);
                float guidanceDepth = LanderMath.GetGuidanceDepth(graph, graphDepth);
                ThrustSolution graphThrust = LanderMath.CalculateThrust(1f, .25f, graphX, guidanceX);
                if (!LanderMath.TryFindNextPlatformCollision(graph, graphDepth, graphX, graphThrust.EndX,
                    guidanceDepth, out PlatformCollision graphCollision) ||
                    !graphCollision.Platform.IsCorrectRoute || graphCollision.Platform.RouteStep != step)
                    throw new InvalidOperationException($"Correct graph route must reach calculated platform {step + 1}.");
                if (graphCollision.Platform.IsCheckpoint)
                {
                    collectedCheckpoints++;
                    AssertNear(graphCollision.ShipX, graphCollision.Platform.CoinX,
                        $"checkpoint {collectedCheckpoints} coin step");
                }
                graphX = graphCollision.ShipX;
                graphDepth = graphCollision.WorldDepth;
            }
            if (collectedCheckpoints != LanderMath.CheckpointCount)
                throw new InvalidOperationException("Correct route must cross exactly three checkpoints.");
            float finalGuidance = LanderMath.GetGuidanceTarget(graph, graphDepth);
            AssertNear(finalGuidance, graph.TargetX, "final graph gradient target");
            ThrustSolution finalGraphThrust = LanderMath.CalculateThrust(1f, .25f, graphX, finalGuidance);
            LanderRunResult finalGraph = LanderMath.ScoreGroundLanding(finalGraphThrust, graph);
            if (!finalGraph.HitFinalTarget)
                throw new InvalidOperationException("The unique complete gradient route must end on the ground target.");

            Debug.Log("Gradient Lander model validation passed.");
        }

        [MenuItem("Gradient Lander/Build Windows Player")]
        public static void BuildWindowsPlayer()
        {
            CreateProject();
            ValidateModel();
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/GradientLander.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows build failed: {report.summary.result}");

            Debug.Log($"Gradient Lander Windows build succeeded ({report.summary.totalSize} bytes).");
        }

        private static void AssertNear(float actual, float expected, string label)
        {
            if (Mathf.Abs(actual - expected) > 0.0001f)
                throw new InvalidOperationException($"Model validation failed for {label}: expected {expected}, got {actual}");
        }
    }
}
