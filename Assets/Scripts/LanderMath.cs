using System;
using System.Collections.Generic;
using UnityEngine;

namespace GradientLander
{
    public enum MissionMode { Random, GraphChallenge }

    public static class LanderMath
    {
        public const float Lambda = 0.1f;
        public const float StartX = 0.23f;
        public const float MissionBudget = 4f;
        public const float CoinReward = 1f;
        public const float CoinTolerance = .052f;
        public const float FinalTargetTolerance = 0.055f;
        public const float PlatformHalfWidth = 0.062f;
        public const float ShipCollisionHalfWidth = 0.026f;
        public const float ShipDeckOffset = 0.063f;
        public const float FrameTopY = 0.68f;
        public const float GroundDeckY = 0.10f;
        public const float GroundShipY = GroundDeckY + ShipDeckOffset;
        public const float FrameScreenSpan = FrameTopY - GroundShipY;
        public const float CameraFollowDepth = 0.35f;
        public const int FrameCount = 4;
        public const int ChallengeDepth = 9;
        public const int GraphLandingCount = 6;
        public const int GraphIterationsPerLanding = 2;
        public const int CheckpointCount = 3;
        public const int GraphAmbientDecoyCount = 5;
        public const int DemonstrationSeed = 1337;

        public const float GraphDomainMin = -2.1f;
        public const float GraphDomainMax = 1.1f;
        public const float GraphLearningRate = .022f;

        private static readonly float[] PlatformHeightTiers = { .55f, .35f, .16f };

        public static MissionLayout GenerateLayout(int seed) => GenerateRandomLayout(seed);

        public static MissionLayout GenerateRandomLayout(int seed)
        {
            System.Random random = new System.Random(seed);
            MissionLayout layout = new MissionLayout
            {
                Seed = seed,
                Mode = MissionMode.Random,
                DisplayName = "Random",
                StartX = StartX,
                TotalDepth = FrameCount,
                TargetX = Mathf.Lerp(.17f, .83f, (float)random.NextDouble()),
                Platforms = new PlatformDefinition[FrameCount][],
                CorrectPath = Array.Empty<Vector2>(),
                CoinCollected = Array.Empty<bool>()
            };

            for (int frame = 0; frame < FrameCount; frame++)
            {
                layout.Platforms[frame] = new PlatformDefinition[PlatformHeightTiers.Length];
                for (int platform = 0; platform < PlatformHeightTiers.Length; platform++)
                {
                    float x = Mathf.Lerp(.11f, .89f, (float)random.NextDouble());
                    float jitter = Mathf.Lerp(-.025f, .025f, (float)random.NextDouble());
                    layout.Platforms[frame][platform] = new PlatformDefinition(
                        frame, platform, x, PlatformHeightTiers[platform] + jitter);
                }
            }
            if (seed == DemonstrationSeed) PrepareDemonstrationRoute(layout);
            return layout;
        }

        public static MissionLayout GenerateGraphChallengeLayout()
        {
            float[] graphSteps = new float[GraphLandingCount + 2];
            // Begin just left of the graph's middle crest. The derivative first moves the
            // route gently left, then accelerates into and oscillates around the deep valley.
            // This produces a visibly curved gradient route rather than a vertical drop.
            graphSteps[0] = -.8f;
            for (int i = 1; i < graphSteps.Length; i++)
            {
                float next = graphSteps[i - 1];
                for (int iteration = 0; iteration < GraphIterationsPerLanding; iteration++)
                    next -= GraphLearningRate * GraphDerivative(next);
                graphSteps[i] = next;
            }

            float[] routeDepths =
            {
                1.15f, 2.45f, 3.75f,
                5.05f, 6.35f, 7.65f
            };
            int[] checkpointSteps = { 1, 3, 5 };
            Vector2[] correctPath = new Vector2[GraphLandingCount + 2];
            correctPath[0] = new Vector2(NormalizeGraphX(graphSteps[0]), 0f);
            for (int i = 0; i < GraphLandingCount; i++)
                correctPath[i + 1] = new Vector2(NormalizeGraphX(graphSteps[i + 1]), routeDepths[i]);
            correctPath[correctPath.Length - 1] = new Vector2(
                NormalizeGraphX(graphSteps[graphSteps.Length - 1]), ChallengeDepth);

            MissionLayout layout = new MissionLayout
            {
                Seed = 0,
                Mode = MissionMode.GraphChallenge,
                DisplayName = "Quartic Valley",
                StartX = correctPath[0].x,
                TotalDepth = ChallengeDepth,
                TargetX = correctPath[correctPath.Length - 1].x,
                Platforms = new PlatformDefinition[ChallengeDepth][],
                CorrectPath = correctPath,
                CoinCollected = new bool[CheckpointCount]
            };

            List<PlatformDefinition>[] sections = new List<PlatformDefinition>[ChallengeDepth];
            for (int section = 0; section < ChallengeDepth; section++)
                sections[section] = new List<PlatformDefinition>();

            for (int step = 0; step < GraphLandingCount; step++)
            {
                float depth = routeDepths[step];
                int section = Mathf.Clamp(Mathf.FloorToInt(depth), 0, ChallengeDepth - 1);
                float routeX = correctPath[step + 1].x;
                int checkpointIndex = Array.IndexOf(checkpointSteps, step);
                if (checkpointIndex >= 0)
                {
                    PlatformDefinition checkpoint = new PlatformDefinition(
                        section, sections[section].Count, routeX, DeckYAtWorldDepth(section, depth))
                    {
                        HalfWidth = .5f,
                        IsCheckpoint = true,
                        IsCorrectRoute = true,
                        RouteStep = step,
                        HasCoin = true,
                        CoinIndex = checkpointIndex,
                        CoinX = routeX
                    };
                    sections[section].Add(checkpoint);
                    continue;
                }

                // One visually ordinary correct platform plus one equally styled decoy.
                // Their positions are deliberately shuffled at every level so no open corridor
                // or repeated lane exposes the computed solution.
                PlatformDefinition routePlatform = new PlatformDefinition(
                    section, sections[section].Count, routeX, DeckYAtWorldDepth(section, depth))
                {
                    IsCorrectRoute = true,
                    RouteStep = step
                };
                sections[section].Add(routePlatform);
                AddDecoyRow(sections[section], section, step, depth, routeX, 1);
            }

            AddAmbientDecoys(sections);

            for (int section = 0; section < ChallengeDepth; section++)
            {
                PlatformDefinition[] sectionPlatforms = sections[section].ToArray();
                Array.Sort(sectionPlatforms, (a, b) =>
                {
                    int stepOrder = a.RouteStep.CompareTo(b.RouteStep);
                    return stepOrder != 0 ? stepOrder : a.X.CompareTo(b.X);
                });
                for (int index = 0; index < sectionPlatforms.Length; index++)
                {
                    PlatformDefinition platform = sectionPlatforms[index];
                    platform.PlatformIndex = index;
                    sectionPlatforms[index] = platform;
                }
                layout.Platforms[section] = sectionPlatforms;
            }
            return layout;
        }

        private static void AddAmbientDecoys(List<PlatformDefinition>[] sections)
        {
            // These extra platforms occupy the quieter gaps between gradient stops. They use
            // ordinary styling and varied lanes, making several routes look plausible without
            // crowding any one screen or changing the single ordered solution.
            float[] depths = { .65f, 1.82f, 4.38f, 6.92f, 8.35f };
            float[] positions = { .16f, .72f, .56f, .46f, .73f };
            for (int i = 0; i < GraphAmbientDecoyCount; i++)
            {
                float depth = depths[i];
                int section = Mathf.Clamp(Mathf.FloorToInt(depth), 0, ChallengeDepth - 1);
                sections[section].Add(new PlatformDefinition(
                    section, sections[section].Count, positions[i], DeckYAtWorldDepth(section, depth)));
            }
        }

        private static void AddDecoyRow(
            List<PlatformDefinition> destination, int section, int step, float depth, float routeX, int count)
        {
            List<float> chosen = new List<float>();
            for (int attempt = 0; attempt < 30 && chosen.Count < count; attempt++)
            {
                float candidate = .07f + Mathf.Repeat(.173f * step + .271f * attempt + .09f, .86f);
                if (Mathf.Abs(candidate - routeX) < .11f) continue;
                bool overlaps = false;
                for (int i = 0; i < chosen.Count; i++)
                    if (Mathf.Abs(candidate - chosen[i]) < .13f) overlaps = true;
                if (overlaps) continue;
                chosen.Add(candidate);
            }

            // The normal constraints nearly always produce four lanes. This deterministic
            // fallback keeps every row visually populated at extreme route positions.
            float[] fallback = { .08f, .25f, .43f, .61f, .79f, .92f };
            for (int i = 0; i < fallback.Length && chosen.Count < count; i++)
            {
                float candidate = fallback[(i + step) % fallback.Length];
                if (Mathf.Abs(candidate - routeX) < .105f) continue;
                bool overlaps = false;
                for (int j = 0; j < chosen.Count; j++)
                    if (Mathf.Abs(candidate - chosen[j]) < .115f) overlaps = true;
                if (!overlaps) chosen.Add(candidate);
            }

            for (int i = 0; i < chosen.Count; i++)
            {
                PlatformDefinition decoy = new PlatformDefinition(
                    section, destination.Count, chosen[i], DeckYAtWorldDepth(section, depth))
                {
                    RouteStep = step
                };
                destination.Add(decoy);
            }
        }

        public static float GraphObjective(float x)
        {
            // Numerically integrate the smooth derivative from 0. The exponential weighting
            // reproduces the supplied graph's deep left valley and shallow valley near x = 0.
            const int slices = 96;
            float step = x / slices;
            float sum = 0f;
            for (int i = 0; i < slices; i++)
                sum += GraphDerivative((i + .5f) * step) * step;
            return -3f + sum;
        }

        public static float GraphDerivative(float x)
        {
            float fittedSlope = 3.872364f * x * (x + .7f) * (x + 1.55f) * Mathf.Exp(-1.8f * x);
            // The supplied curve rises sharply to the right of its shallow minimum.
            if (x > 0f) fittedSlope += 16f * x * x * x;
            return fittedSlope;
        }

        public static float NormalizeGraphX(float graphX)
        {
            return Mathf.InverseLerp(GraphDomainMin, GraphDomainMax, graphX);
        }

        public static float GetGuidanceTarget(MissionLayout layout, float worldDepth)
        {
            if (layout.Mode != MissionMode.GraphChallenge || layout.CorrectPath == null)
                return layout.TargetX;
            for (int i = 1; i < layout.CorrectPath.Length; i++)
                if (layout.CorrectPath[i].y > worldDepth + .015f) return layout.CorrectPath[i].x;
            return layout.TargetX;
        }

        public static float GetGuidanceDepth(MissionLayout layout, float worldDepth)
        {
            if (layout.Mode != MissionMode.GraphChallenge || layout.CorrectPath == null)
                return layout.TotalDepth;
            for (int i = 1; i < layout.CorrectPath.Length; i++)
                if (layout.CorrectPath[i].y > worldDepth + .015f) return layout.CorrectPath[i].y;
            return layout.TotalDepth;
        }

        private static float DeckYAtWorldDepth(int frame, float worldDepth)
        {
            return FrameTopY - (worldDepth - frame) * FrameScreenSpan - ShipDeckOffset;
        }

        private static void PrepareDemonstrationRoute(MissionLayout layout)
        {
            const float weight = .8f;
            float startX = layout.StartX;
            ThrustSolution firstFrame = CalculateThrust(weight, .55f, startX, layout.TargetX);
            for (int i = 0; i < layout.Platforms[0].Length; i++)
            {
                PlatformDefinition platform = layout.Platforms[0][i];
                float depth = GetPlatformWorldDepth(platform);
                float pathX = EvaluateContinuousPathX(startX, firstFrame.EndX, 0f, depth);
                platform.X = pathX < .5f ? .89f : .11f;
                layout.Platforms[0][i] = platform;
            }
            PlatformDefinition landingPlatform = layout.Platforms[1][0];
            float platformDepth = GetPlatformWorldDepth(landingPlatform);
            landingPlatform.X = Mathf.Clamp(
                EvaluateContinuousPathX(startX, firstFrame.EndX, 0f, platformDepth), .11f, .89f);
            layout.Platforms[1][0] = landingPlatform;
        }

        public static PlatformDefinition[] GetPlatforms(MissionLayout layout, int frameIndex)
        {
            return layout.Platforms[Mathf.Clamp(frameIndex, 0, layout.Platforms.Length - 1)];
        }

        public static float GetPlatformWorldDepth(PlatformDefinition platform)
        {
            float shipYAtPlatform = platform.DeckY + ShipDeckOffset;
            float frameProgress = (FrameTopY - shipYAtPlatform) / FrameScreenSpan;
            return platform.FrameIndex + Mathf.Clamp01(frameProgress);
        }

        public static float GetCameraDepth(float shipWorldDepth) => Mathf.Max(0f, shipWorldDepth - CameraFollowDepth);

        public static float GetShipScreenY(float shipWorldDepth)
        {
            float cameraDepth = GetCameraDepth(shipWorldDepth);
            return FrameTopY - (shipWorldDepth - cameraDepth) * FrameScreenSpan;
        }

        public static ThrustSolution CalculateThrust(float weight, float bias, float startX, float guidanceTargetX)
        {
            float error = guidanceTargetX - startX;
            float leftPower = bias + weight * error;
            float rightPower = bias - weight * error;
            float rawEndX = startX + .5f * (leftPower - rightPower);
            return new ThrustSolution
            {
                Weight = weight, Bias = bias, StartX = startX, GuidanceTargetX = guidanceTargetX,
                InitialError = error, LeftPower = leftPower, RightPower = rightPower,
                TotalPower = leftPower + rightPower, RawEndX = rawEndX,
                EndX = Mathf.Clamp(rawEndX, .035f, .965f)
            };
        }

        public static float EvaluateContinuousPathX(float startX, float destinationX, float startWorldDepth, float worldDepth)
        {
            return EvaluatePathX(startX, destinationX, startWorldDepth, FrameCount, worldDepth);
        }

        public static float EvaluatePathX(
            float startX, float destinationX, float startWorldDepth, float destinationWorldDepth, float worldDepth)
        {
            float progress = Mathf.InverseLerp(startWorldDepth, destinationWorldDepth, worldDepth);
            return Mathf.Lerp(startX, destinationX, SmoothStep(progress));
        }

        public static bool TryFindNextPlatformCollision(
            MissionLayout layout, float startWorldDepth, float startX, float destinationX,
            float destinationWorldDepth, out PlatformCollision collision)
        {
            bool found = false;
            PlatformCollision nearest = default;
            for (int frameIndex = 0; frameIndex < layout.Platforms.Length; frameIndex++)
            {
                PlatformDefinition[] platforms = GetPlatforms(layout, frameIndex);
                for (int i = 0; i < platforms.Length; i++)
                {
                    PlatformDefinition platform = platforms[i];
                    float platformDepth = GetPlatformWorldDepth(platform);
                    if (platformDepth <= startWorldDepth + .018f || (found && platformDepth >= nearest.WorldDepth)) continue;
                    float shipX = EvaluatePathX(startX, destinationX, startWorldDepth, destinationWorldDepth, platformDepth);
                    float collisionRange = platform.HalfWidth + ShipCollisionHalfWidth;
                    if (!platform.IsCheckpoint && Mathf.Abs(shipX - platform.X) > collisionRange) continue;
                    found = true;
                    nearest = new PlatformCollision
                    {
                        Platform = platform,
                        PathProgress = Mathf.InverseLerp(startWorldDepth, layout.TotalDepth, platformDepth),
                        ShipX = shipX,
                        WorldDepth = platformDepth
                    };
                }
            }
            collision = nearest;
            return found;
        }

        public static LanderRunResult ScorePlatformLanding(ThrustSolution thrust, PlatformCollision collision)
        {
            LanderRunResult result = ScoreLanding(thrust, collision.ShipX, collision.Platform.X, collision.Platform.FrameIndex,
                collision.Platform.PlatformIndex, collision.WorldDepth, false);
            result.IsCheckpoint = collision.Platform.IsCheckpoint;
            result.CheckpointIndex = collision.Platform.CoinIndex;
            result.RouteStep = collision.Platform.RouteStep;
            return result;
        }

        public static LanderRunResult ScoreGroundLanding(ThrustSolution thrust, MissionLayout layout)
        {
            return ScoreLanding(thrust, thrust.EndX, layout.TargetX,
                layout.Platforms.Length - 1, -1, layout.TotalDepth, true);
        }

        private static LanderRunResult ScoreLanding(
            ThrustSolution thrust, float landingX, float costTargetX, int frameIndex,
            int platformIndex, float worldDepth, bool isGround)
        {
            float distance = Mathf.Abs(landingX - costTargetX);
            float powerCost = thrust.LeftPower * thrust.LeftPower + thrust.RightPower * thrust.RightPower;
            return new LanderRunResult
            {
                Weight = thrust.Weight, Bias = thrust.Bias, StageIndex = frameIndex, PlatformIndex = platformIndex,
                StartX = thrust.StartX, TargetX = costTargetX, InitialError = thrust.InitialError,
                LeftPower = thrust.LeftPower, RightPower = thrust.RightPower, TotalPower = thrust.TotalPower,
                RawLandingX = thrust.RawEndX, LandingX = landingX, WorldDepth = worldDepth,
                Distance = distance, PowerCost = powerCost, Cost = distance * distance + Lambda * powerCost,
                HitBoundary = !Mathf.Approximately(thrust.RawEndX, thrust.EndX), IsGroundLanding = isGround
            };
        }

        public static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }

    [Serializable]
    public sealed class MissionLayout
    {
        public int Seed;
        public MissionMode Mode;
        public string DisplayName;
        public float StartX;
        public float TotalDepth;
        public float TargetX;
        public PlatformDefinition[][] Platforms;
        public Vector2[] CorrectPath;
        public bool[] CoinCollected;
    }

    [Serializable]
    public struct PlatformDefinition
    {
        public int FrameIndex;
        public int PlatformIndex;
        public float X;
        public float DeckY;
        public float HalfWidth;
        public bool IsCheckpoint;
        public bool IsCorrectRoute;
        public int RouteStep;
        public bool HasCoin;
        public int CoinIndex;
        public float CoinX;

        public PlatformDefinition(int frameIndex, int platformIndex, float x, float deckY)
        {
            FrameIndex = frameIndex; PlatformIndex = platformIndex; X = x; DeckY = deckY;
            HalfWidth = LanderMath.PlatformHalfWidth; IsCheckpoint = false; IsCorrectRoute = false;
            RouteStep = -1; HasCoin = false;
            CoinIndex = -1; CoinX = 0f;
        }

        public string Label => IsCheckpoint ? $"CHECKPOINT {CoinIndex + 1}" : $"F{FrameIndex + 1}-{(char)('A' + PlatformIndex)}";
    }

    [Serializable]
    public struct PlatformCollision
    {
        public PlatformDefinition Platform;
        public float PathProgress;
        public float ShipX;
        public float WorldDepth;
    }

    [Serializable]
    public struct ThrustSolution
    {
        public float Weight, Bias, StartX, GuidanceTargetX, InitialError;
        public float LeftPower, RightPower, TotalPower, RawEndX, EndX;
    }

    [Serializable]
    public struct LanderRunResult
    {
        public float Weight, Bias;
        public int StageIndex, PlatformIndex;
        public float StartX, TargetX, InitialError, LeftPower, RightPower, TotalPower;
        public float RawLandingX, LandingX, WorldDepth, Distance, PowerCost, Cost;
        public bool HitBoundary, IsGroundLanding;
        public bool IsCheckpoint;
        public int CheckpointIndex, RouteStep;
        public bool IsNearLanding => Distance <= .07f;
        public bool HitFinalTarget => IsGroundLanding && Distance <= LanderMath.FinalTargetTolerance;
    }
}
