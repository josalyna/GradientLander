using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GradientLander
{
    internal static class MeshShapes
    {
        public static void Quad(VertexHelper vh, Rect r, Color color)
        {
            int i = vh.currentVertCount;
            AddVert(vh, new Vector2(r.xMin, r.yMin), color);
            AddVert(vh, new Vector2(r.xMin, r.yMax), color);
            AddVert(vh, new Vector2(r.xMax, r.yMax), color);
            AddVert(vh, new Vector2(r.xMax, r.yMin), color);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void GradientQuad(VertexHelper vh, Rect r, Color bottom, Color top)
        {
            int i = vh.currentVertCount;
            AddVert(vh, new Vector2(r.xMin, r.yMin), bottom);
            AddVert(vh, new Vector2(r.xMin, r.yMax), top);
            AddVert(vh, new Vector2(r.xMax, r.yMax), top);
            AddVert(vh, new Vector2(r.xMax, r.yMin), bottom);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Circle(VertexHelper vh, Vector2 center, float radius, Color color, int segments = 28)
        {
            Ellipse(vh, center, new Vector2(radius, radius), color, segments);
        }

        public static void Ellipse(VertexHelper vh, Vector2 center, Vector2 radius, Color color, int segments = 32)
        {
            int start = vh.currentVertCount;
            AddVert(vh, center, color);
            for (int n = 0; n <= segments; n++)
            {
                float a = n * Mathf.PI * 2f / segments;
                AddVert(vh, center + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y), color);
            }
            for (int n = 0; n < segments; n++) vh.AddTriangle(start, start + n + 1, start + n + 2);
        }

        public static void Ring(VertexHelper vh, Vector2 center, Vector2 outer, Vector2 inner, Color color, int segments = 36)
        {
            int start = vh.currentVertCount;
            for (int n = 0; n <= segments; n++)
            {
                float a = n * Mathf.PI * 2f / segments;
                Vector2 direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddVert(vh, center + new Vector2(direction.x * outer.x, direction.y * outer.y), color);
                AddVert(vh, center + new Vector2(direction.x * inner.x, direction.y * inner.y), color);
            }
            for (int n = 0; n < segments; n++)
            {
                int o = start + n * 2;
                vh.AddTriangle(o, o + 2, o + 1);
                vh.AddTriangle(o + 2, o + 3, o + 1);
            }
        }

        public static void Polygon(VertexHelper vh, IReadOnlyList<Vector2> points, Color color)
        {
            if (points == null || points.Count < 3) return;
            int start = vh.currentVertCount;
            for (int n = 0; n < points.Count; n++) AddVert(vh, points[n], color);
            for (int n = 1; n < points.Count - 1; n++) vh.AddTriangle(start, start + n, start + n + 1);
        }

        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * width * .5f;
            Polygon(vh, new[] { a - normal, a + normal, b + normal, b - normal }, color);
        }

        private static void AddVert(VertexHelper vh, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vh.AddVert(vertex);
        }
    }

    /// <summary>
    /// Draws the entire randomized map in world-depth coordinates. Once the lander
    /// passes the camera threshold, all scenery scrolls upward continuously.
    /// </summary>
    public sealed class LandingFieldGraphic : MaskableGraphic
    {
        private MissionLayout layout;
        private float shipWorldDepth;
        private bool flying;
        private float trailProgress;
        private float trailStartDepth;
        private float trailEndDepth;
        private float trailStartX;
        private float trailDestinationX;
        private float trailDestinationDepth;
        private bool reviewing;
        private float overviewProgress;
        private IReadOnlyList<Vector2> actualPath;
        private IReadOnlyList<Vector2> correctPath;
        private IReadOnlyList<Vector2> livePath;

        private static readonly Color Accent = new Color32(255, 184, 75, 255);
        private static readonly Color Cyan = new Color32(91, 205, 232, 255);
        private static readonly Color Target = new Color32(239, 74, 92, 255);
        private static readonly Color PlatformTop = new Color32(150, 175, 205, 255);
        private static readonly Color PlatformBody = new Color32(46, 61, 83, 255);

        public void SetReady(MissionLayout missionLayout, float worldDepth, float shipX)
        {
            layout = missionLayout;
            shipWorldDepth = worldDepth;
            flying = false;
            reviewing = false;
            trailStartX = trailDestinationX = shipX;
            trailProgress = 0f;
            SetVerticesDirty();
        }

        public void SetFlight(
            MissionLayout missionLayout,
            float currentWorldDepth,
            float fromDepth,
            float toDepth,
            float fromX,
            float groundDestinationX,
            float destinationDepth,
            float progress)
        {
            layout = missionLayout;
            shipWorldDepth = currentWorldDepth;
            flying = true;
            reviewing = false;
            trailStartDepth = fromDepth;
            trailEndDepth = toDepth;
            trailStartX = fromX;
            trailDestinationX = groundDestinationX;
            trailDestinationDepth = destinationDepth;
            trailProgress = Mathf.Clamp01(progress);
            SetVerticesDirty();
        }

        public void SetComplete(MissionLayout missionLayout, float worldDepth, float shipX)
        {
            layout = missionLayout;
            shipWorldDepth = worldDepth;
            flying = false;
            reviewing = false;
            trailStartX = trailDestinationX = shipX;
            trailProgress = 1f;
            SetVerticesDirty();
        }

        public void SetReview(
            MissionLayout missionLayout,
            IReadOnlyList<Vector2> flownPath,
            IReadOnlyList<Vector2> solutionPath,
            float zoomProgress)
        {
            layout = missionLayout;
            shipWorldDepth = missionLayout.TotalDepth;
            flying = false;
            reviewing = true;
            actualPath = flownPath;
            correctPath = solutionPath;
            overviewProgress = LanderMath.SmoothStep(zoomProgress);
            SetVerticesDirty();
        }

        public void SetPersistentPath(IReadOnlyList<Vector2> flownPath)
        {
            livePath = flownPath;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float cameraDepth = LanderMath.GetCameraDepth(shipWorldDepth);
            DrawAtmosphere(vh, r, cameraDepth, flying);
            if (layout != null)
            {
                for (int frame = 0; frame < layout.Platforms.Length; frame++)
                {
                    PlatformDefinition[] platforms = LanderMath.GetPlatforms(layout, frame);
                    for (int i = 0; i < platforms.Length; i++) DrawPlatform(vh, r, platforms[i], cameraDepth);
                }
                DrawGround(vh, r, layout.TargetX, cameraDepth);
            }
            if (!reviewing)
            {
                DrawReviewPath(vh, r, livePath, new Color32(14, 45, 68, 210), 7f, false);
                DrawReviewPath(vh, r, livePath, new Color32(91, 205, 232, 245), 3.5f, false);
            }
            if (reviewing)
            {
                DrawReviewPath(vh, r, correctPath, new Color32(255, 184, 75, 225), 3.5f, false);
                DrawReviewPath(vh, r, actualPath, new Color32(91, 205, 232, 255), 4.5f, false);
            }
        }

        private void DrawAtmosphere(VertexHelper vh, Rect r, float cameraDepth, bool inFlight)
        {
            float totalDepth = layout != null ? layout.TotalDepth : LanderMath.FrameCount;
            float normalized = Mathf.Clamp01(cameraDepth / totalDepth);
            Color highTop = new Color32(5, 12, 34, 255);
            Color highBottom = new Color32(15, 30, 63, 255);
            Color lowTop = new Color32(30, 25, 57, 255);
            Color lowBottom = new Color32(69, 55, 78, 255);
            MeshShapes.GradientQuad(vh, r, Color.Lerp(highBottom, lowBottom, normalized), Color.Lerp(highTop, lowTop, normalized));

            float starAlpha = Mathf.Lerp(1f, .20f, normalized);
            for (int n = 0; n < 70; n++)
            {
                float px = Mathf.Repeat(n * .6180339f + .07f, 1f);
                float py = Mathf.Repeat(n * .381966f + .13f + cameraDepth * .24f, .92f) + .04f;
                float size = .7f + (n % 5) * .32f;
                MeshShapes.Circle(vh, new Vector2(r.xMin + px * r.width, r.yMin + py * r.height), size,
                    new Color(.72f, .84f, 1f, starAlpha * (.2f + (n % 4) * .13f)), 8);
            }

            if (cameraDepth < 1.15f)
            {
                float fade = 1f - Mathf.Clamp01(cameraDepth / 1.15f);
                Vector2 planet = new Vector2(r.xMin + r.width * .11f, r.yMax - r.height * (.34f + cameraDepth * .11f));
                MeshShapes.Circle(vh, planet, Mathf.Min(r.width, r.height) * .053f, new Color(1f, .76f, .30f, fade), 38);
                MeshShapes.Circle(vh, planet + new Vector2(-11f, 8f), 8f, new Color(.68f, .45f, .15f, fade * .55f), 18);
            }

            for (int n = 0; n < 5; n++)
            {
                float y = Mathf.Repeat(.15f + n * .23f + cameraDepth * .25f, 1f);
                float alpha = Mathf.Lerp(.015f, .10f, normalized) * (1f - Mathf.Abs(.5f - y));
                MeshShapes.Quad(vh, new Rect(r.xMin, r.yMin + y * r.height, r.width, 2f + normalized * 5f), new Color(.65f, .67f, .85f, alpha));
            }

            if (inFlight)
            {
                for (int n = 0; n < 15; n++)
                {
                    float x = r.xMin + Mathf.Repeat(n * .279f + .08f, 1f) * r.width;
                    float y = r.yMin + Mathf.Repeat(n * .413f + cameraDepth * .31f, 1f) * r.height;
                    MeshShapes.Line(vh, new Vector2(x, y), new Vector2(x, y + 17f), 1.1f, new Color(.65f, .85f, 1f, .11f));
                }
            }
        }

        private float DepthToY(float depth, float cameraDepth)
        {
            float normal = LanderMath.FrameTopY - (depth - cameraDepth) * LanderMath.FrameScreenSpan;
            float totalDepth = layout != null ? layout.TotalDepth : LanderMath.FrameCount;
            float overview = Mathf.Lerp(.90f, .10f, depth / totalDepth);
            return reviewing ? Mathf.Lerp(normal, overview, overviewProgress) : normal;
        }

        private void DrawPlatform(VertexHelper vh, Rect r, PlatformDefinition platform, float cameraDepth)
        {
            float platformDepth = LanderMath.GetPlatformWorldDepth(platform);
            float shipCenterY = DepthToY(platformDepth, cameraDepth);
            float yNormalized = shipCenterY - LanderMath.ShipDeckOffset;
            float y = r.yMin + yNormalized * r.height;
            if (y < r.yMin - 45f || y > r.yMax + 45f) return;

            float x = r.xMin + (platform.IsCheckpoint ? .5f : platform.X) * r.width;
            float deckWidth = r.width * (platform.IsCheckpoint ? .94f : platform.HalfWidth * 2f);
            Color body = platform.IsCheckpoint ? new Color32(39, 67, 85, 255) : PlatformBody;
            Color top = platform.IsCheckpoint ? Cyan : PlatformTop;
            MeshShapes.Quad(vh, new Rect(x - deckWidth * .5f, y - 10f, deckWidth, 12f), body);
            MeshShapes.Quad(vh, new Rect(x - deckWidth * .5f, y, deckWidth, platform.IsCheckpoint ? 5f : 3f), top);
            if (platform.IsCheckpoint)
            {
                for (int mark = 1; mark < 10; mark++)
                {
                    float mx = x - deckWidth * .5f + deckWidth * mark / 10f;
                    MeshShapes.Quad(vh, new Rect(mx - 1f, y - 9f, 2f, 9f), new Color32(22, 42, 62, 180));
                }
            }
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(x - deckWidth * .34f, y - 10f), new Vector2(x - deckWidth * .23f, y - 24f),
                new Vector2(x - deckWidth * .16f, y - 24f), new Vector2(x - deckWidth * .07f, y - 10f)
            }, new Color32(32, 44, 64, 255));
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(x + deckWidth * .07f, y - 10f), new Vector2(x + deckWidth * .16f, y - 24f),
                new Vector2(x + deckWidth * .23f, y - 24f), new Vector2(x + deckWidth * .34f, y - 10f)
            }, new Color32(32, 44, 64, 255));
            MeshShapes.Ring(vh, new Vector2(x, y + 4f), new Vector2(13f, 5f), new Vector2(7f, 2.5f), Cyan, 20);
            MeshShapes.Circle(vh, new Vector2(x, y + 4f), 2.2f, Accent, 10);

            if (platform.HasCoin && layout != null &&
                platform.CoinIndex >= 0 && platform.CoinIndex < layout.CoinCollected.Length &&
                !layout.CoinCollected[platform.CoinIndex])
            {
                Vector2 coin = new Vector2(r.xMin + platform.CoinX * r.width, y + 28f);
                MeshShapes.Circle(vh, coin, 15f, new Color32(255, 199, 55, 70), 28);
                MeshShapes.Circle(vh, coin, 10f, new Color32(255, 193, 47, 255), 28);
                MeshShapes.Ring(vh, coin, new Vector2(7f, 7f), new Vector2(4f, 4f), new Color32(255, 235, 140, 255), 22);
            }
        }

        private void DrawGround(VertexHelper vh, Rect r, float targetX, float cameraDepth)
        {
            float groundShipCenterY = DepthToY(layout.TotalDepth, cameraDepth);
            float groundYNormalized = groundShipCenterY - LanderMath.ShipDeckOffset;
            float groundY = r.yMin + groundYNormalized * r.height;
            if (groundY < r.yMin - r.height * .2f || groundY > r.yMax + 80f) return;

            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(r.xMin, groundY), new Vector2(r.xMin + r.width * .13f, groundY + 43f),
                new Vector2(r.xMin + r.width * .27f, groundY + 14f), new Vector2(r.xMin + r.width * .43f, groundY + 58f),
                new Vector2(r.xMin + r.width * .59f, groundY + 17f), new Vector2(r.xMin + r.width * .77f, groundY + 37f),
                new Vector2(r.xMax, groundY + 10f), new Vector2(r.xMax, groundY), new Vector2(r.xMin, groundY)
            }, new Color32(36, 42, 57, 255));
            MeshShapes.Quad(vh, new Rect(r.xMin, r.yMin, r.width, Mathf.Max(0f, groundY - r.yMin)), new Color32(54, 61, 76, 255));
            MeshShapes.Quad(vh, new Rect(r.xMin, groundY - 3f, r.width, 4f), new Color32(93, 99, 113, 255));
            MeshShapes.Ellipse(vh, new Vector2(r.xMin + r.width * .18f, groundY - 35f), new Vector2(r.width * .06f, 12f), new Color32(39, 44, 57, 180), 24);
            MeshShapes.Ellipse(vh, new Vector2(r.xMin + r.width * .88f, groundY - 25f), new Vector2(r.width * .08f, 15f), new Color32(39, 44, 57, 180), 24);

            // The target is created only here, physically attached to the ground.
            Vector2 target = new Vector2(r.xMin + targetX * r.width, groundY + 7f);
            MeshShapes.Ellipse(vh, target, new Vector2(r.width * .078f, 25f), new Color(.94f, .25f, .34f, .20f), 38);
            MeshShapes.Ring(vh, target, new Vector2(r.width * .053f, 18f), new Vector2(r.width * .036f, 10f), Target, 38);
            MeshShapes.Ellipse(vh, target, new Vector2(r.width * .011f, 5f), Accent, 20);
            MeshShapes.Line(vh, target + new Vector2(0f, 27f), target + new Vector2(0f, 68f), 2f, new Color32(239, 74, 92, 210));
        }

        private void DrawTrajectory(VertexHelper vh, Rect r, float cameraDepth)
        {
            const int dots = 28;
            for (int n = 0; n < dots; n++)
            {
                float t = n / (dots - 1f);
                if (t > trailProgress) break;
                float eased = LanderMath.SmoothStep(t);
                float depth = Mathf.Lerp(trailStartDepth, trailEndDepth, eased);
                float x = LanderMath.EvaluatePathX(
                    trailStartX, trailDestinationX, trailStartDepth, trailDestinationDepth, depth);
                float y = DepthToY(depth, cameraDepth);
                float alpha = Mathf.Lerp(.10f, .58f, t) * Mathf.Clamp01((trailProgress - t) * 8f + .25f);
                MeshShapes.Circle(vh, new Vector2(r.xMin + x * r.width, r.yMin + y * r.height), 2f, new Color(.45f, .82f, 1f, alpha), 10);
            }
        }

        private void DrawReviewPath(
            VertexHelper vh, Rect r, IReadOnlyList<Vector2> path, Color color, float width, bool dotted)
        {
            if (path == null || path.Count < 2) return;
            float cameraDepth = LanderMath.GetCameraDepth(shipWorldDepth);
            for (int i = 1; i < path.Count; i++)
            {
                if (dotted && i % 2 == 0) continue;
                Vector2 a = new Vector2(r.xMin + path[i - 1].x * r.width,
                    r.yMin + DepthToY(path[i - 1].y, cameraDepth) * r.height);
                Vector2 b = new Vector2(r.xMin + path[i].x * r.width,
                    r.yMin + DepthToY(path[i].y, cameraDepth) * r.height);
                MeshShapes.Line(vh, a, b, width, color);
            }
        }
    }

    public sealed class MapPreviewGraphic : MaskableGraphic
    {
        public MissionMode Mode { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            MeshShapes.GradientQuad(vh, r, new Color32(8, 18, 36, 255), new Color32(17, 32, 59, 255));
            Color grid = new Color32(81, 105, 136, 70);
            for (int i = 1; i < 7; i++)
            {
                float x = Mathf.Lerp(r.xMin, r.xMax, i / 7f);
                MeshShapes.Line(vh, new Vector2(x, r.yMin), new Vector2(x, r.yMax), 1f, grid);
            }
            for (int i = 1; i < 5; i++)
            {
                float y = Mathf.Lerp(r.yMin, r.yMax, i / 5f);
                MeshShapes.Line(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), 1f, grid);
            }

            if (Mode == MissionMode.GraphChallenge) DrawQuartic(vh, r);
            else DrawRandom(vh, r);
        }

        private static void DrawQuartic(VertexHelper vh, Rect r)
        {
            Vector2 previous = default;
            bool hasPrevious = false;
            for (int i = 0; i <= 90; i++)
            {
                float t = i / 90f;
                float gx = Mathf.Lerp(LanderMath.GraphDomainMin, LanderMath.GraphDomainMax, t);
                float gy = LanderMath.GraphObjective(gx);
                float normalizedY = Mathf.InverseLerp(-6.5f, 5f, gy);
                Vector2 point = new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), Mathf.Lerp(r.yMin + 8f, r.yMax - 8f, normalizedY));
                if (hasPrevious && Mathf.Abs(point.y - previous.y) < r.height * .65f)
                    MeshShapes.Line(vh, previous, point, 3f, new Color32(239, 91, 109, 255));
                previous = point;
                hasPrevious = true;
            }
        }

        private static void DrawRandom(VertexHelper vh, Rect r)
        {
            Vector2 previous = default;
            for (int i = 0; i <= 70; i++)
            {
                float t = i / 70f;
                float wave = .50f + Mathf.Sin(t * 17f + .8f) * .18f + Mathf.Sin(t * 39f) * .08f;
                Vector2 point = new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), Mathf.Lerp(r.yMin, r.yMax, wave));
                if (i > 0) MeshShapes.Line(vh, previous, point, 2.5f, new Color32(91, 205, 232, 230));
                previous = point;
            }
            for (int i = 0; i < 12; i++)
            {
                float x = Mathf.Repeat(i * .618f + .11f, 1f);
                float y = Mathf.Repeat(i * .37f + .16f, 1f);
                MeshShapes.Circle(vh, new Vector2(Mathf.Lerp(r.xMin, r.xMax, x), Mathf.Lerp(r.yMin, r.yMax, y)),
                    3f, new Color32(255, 184, 75, 210), 10);
            }
        }
    }

    public enum ShipSkin
    {
        Classic = 0,
        Dog = 1,
        Cat = 2
    }

    public sealed class LanderGraphic : MaskableGraphic
    {
        private float leftPower;
        private float rightPower;
        private bool flying;
        private float pulse;
        private ShipSkin skin;

        public void SetSkin(ShipSkin value)
        {
            skin = value;
            SetVerticesDirty();
        }

        public void SetState(float left, float right, bool isFlying, float flamePulse)
        {
            leftPower = left;
            rightPower = right;
            flying = isFlying;
            pulse = flamePulse;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color outline = new Color32(8, 15, 30, 255);
            Color shell = skin == ShipSkin.Dog
                ? new Color32(244, 223, 184, 255)
                : skin == ShipSkin.Cat ? new Color32(221, 210, 242, 255) : new Color32(224, 233, 242, 255);
            Color shellShade = skin == ShipSkin.Dog
                ? new Color32(188, 139, 91, 255)
                : skin == ShipSkin.Cat ? new Color32(151, 126, 188, 255) : new Color32(150, 169, 191, 255);
            Color glass = new Color32(80, 197, 230, 255);
            Color accent = new Color32(255, 184, 75, 255);

            if (flying)
            {
                DrawFlame(vh, -25f, Mathf.Abs(leftPower), leftPower < 0f, pulse);
                DrawFlame(vh, 25f, Mathf.Abs(rightPower), rightPower < 0f, 1f - pulse);
            }

            if (skin == ShipSkin.Dog)
            {
                Color ear = new Color32(118, 73, 52, 255);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(-8f, 43f), new Vector2(-29f, 61f), new Vector2(-41f, 47f),
                    new Vector2(-36f, 21f), new Vector2(-20f, 28f)
                }, outline);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(-10f, 42f), new Vector2(-28f, 55f), new Vector2(-35f, 44f),
                    new Vector2(-31f, 29f), new Vector2(-20f, 32f)
                }, ear);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(8f, 43f), new Vector2(29f, 61f), new Vector2(41f, 47f),
                    new Vector2(36f, 21f), new Vector2(20f, 28f)
                }, outline);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(10f, 42f), new Vector2(28f, 55f), new Vector2(35f, 44f),
                    new Vector2(31f, 29f), new Vector2(20f, 32f)
                }, ear);
            }
            else if (skin == ShipSkin.Cat)
            {
                MeshShapes.Line(vh, new Vector2(22f, -22f), new Vector2(43f, -12f), 10f, outline);
                MeshShapes.Line(vh, new Vector2(43f, -12f), new Vector2(49f, 7f), 10f, outline);
                MeshShapes.Line(vh, new Vector2(49f, 7f), new Vector2(40f, 20f), 10f, outline);
                MeshShapes.Line(vh, new Vector2(22f, -22f), new Vector2(43f, -12f), 5f, shellShade);
                MeshShapes.Line(vh, new Vector2(43f, -12f), new Vector2(49f, 7f), 5f, shellShade);
                MeshShapes.Line(vh, new Vector2(49f, 7f), new Vector2(40f, 20f), 5f, shellShade);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(-23f, 35f), new Vector2(-20f, 66f), new Vector2(-2f, 50f)
                }, outline);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(23f, 35f), new Vector2(20f, 66f), new Vector2(2f, 50f)
                }, outline);
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(-17f, 43f), new Vector2(-17f, 58f), new Vector2(-7f, 49f)
                }, new Color32(239, 148, 174, 255));
                MeshShapes.Polygon(vh, new[]
                {
                    new Vector2(17f, 43f), new Vector2(17f, 58f), new Vector2(7f, 49f)
                }, new Color32(239, 148, 174, 255));
            }

            MeshShapes.Line(vh, new Vector2(-25f, -33f), new Vector2(-42f, -52f), 5f, outline);
            MeshShapes.Line(vh, new Vector2(25f, -33f), new Vector2(42f, -52f), 5f, outline);
            MeshShapes.Line(vh, new Vector2(-42f, -52f), new Vector2(-50f, -52f), 5f, shellShade);
            MeshShapes.Line(vh, new Vector2(42f, -52f), new Vector2(50f, -52f), 5f, shellShade);
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(0f, 61f), new Vector2(-31f, 3f), new Vector2(-27f, -34f),
                new Vector2(0f, -45f), new Vector2(27f, -34f), new Vector2(31f, 3f)
            }, outline);
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(0f, 53f), new Vector2(-24f, 0f), new Vector2(-21f, -29f),
                new Vector2(0f, -38f), new Vector2(21f, -29f), new Vector2(24f, 0f)
            }, shell);
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(0f, 53f), new Vector2(24f, 0f), new Vector2(21f, -29f),
                new Vector2(7f, -35f), new Vector2(7f, 36f)
            }, shellShade);

            if (skin == ShipSkin.Dog)
            {
                Color spot = new Color32(119, 72, 48, 255);
                MeshShapes.Circle(vh, new Vector2(-13f, -10f), 6f, spot, 18);
                MeshShapes.Circle(vh, new Vector2(11f, -21f), 5f, spot, 18);
                MeshShapes.Circle(vh, new Vector2(8f, 34f), 4.5f, spot, 18);
                MeshShapes.Circle(vh, new Vector2(-13f, 35f), 3.5f, spot, 18);
            }
            MeshShapes.Circle(vh, new Vector2(-2f, 13f), 13f, outline, 28);
            MeshShapes.Circle(vh, new Vector2(-2f, 13f), 9f, glass, 28);
            MeshShapes.Circle(vh, new Vector2(-5f, 17f), 2.5f, new Color(1f, 1f, 1f, .72f), 14);
            MeshShapes.Quad(vh, new Rect(-31f, -38f, 16f, 13f), outline);
            MeshShapes.Quad(vh, new Rect(15f, -38f, 16f, 13f), outline);
            MeshShapes.Quad(vh, new Rect(-28f, -36f, 10f, 8f), accent);
            MeshShapes.Quad(vh, new Rect(18f, -36f, 10f, 8f), accent);
        }

        private static void DrawFlame(VertexHelper vh, float x, float power, bool reverse, float flamePulse)
        {
            float intensity = Mathf.Clamp01(power / 1.5f);
            float length = Mathf.Lerp(12f, 45f, intensity) * Mathf.Lerp(.88f, 1.12f, flamePulse);
            Color outer = reverse ? new Color32(112, 147, 255, 210) : new Color32(255, 107, 61, 220);
            Color inner = reverse ? new Color32(173, 215, 255, 245) : new Color32(255, 219, 91, 245);
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(x - 8f, -37f), new Vector2(x + 8f, -37f), new Vector2(x, -37f - length)
            }, outer);
            MeshShapes.Polygon(vh, new[]
            {
                new Vector2(x - 3.5f, -37f), new Vector2(x + 3.5f, -37f), new Vector2(x, -34f - length * .72f)
            }, inner);
        }
    }
}
