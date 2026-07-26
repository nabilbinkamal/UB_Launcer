using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Launcher.UI.Controls;

internal sealed class MinecraftTexture
{
    private MinecraftTexture(int width, int height, uint[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Pixels { get; }

    public static MinecraftTexture? Decode(Stream stream)
    {
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;

        var sourcePixels = bitmap.Pixels;
        var pixels = new uint[sourcePixels.Length];
        for (var index = 0; index < sourcePixels.Length; index++)
        {
            var color = sourcePixels[index];
            pixels[index] =
                (uint)(color.Alpha << 24) |
                (uint)(color.Red << 16) |
                (uint)(color.Green << 8) |
                color.Blue;
        }

        return new MinecraftTexture(bitmap.Width, bitmap.Height, pixels);
    }

    public bool DetectSlimModel()
    {
        if (Width < 64 || Height < 64)
            return false;

        return IsTransparentStrip(54, 20, 2, 12) ||
               IsTransparentStrip(46, 52, 2, 12);
    }

    public TextureRegion FindOpaqueBounds()
    {
        var minX = Width;
        var minY = Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < Height; y++)
        {
            var rowOffset = y * Width;
            for (var x = 0; x < Width; x++)
            {
                if ((Pixels[rowOffset + x] >> 24) <= 8)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX >= minX && maxY >= minY
            ? new TextureRegion(minX, minY, maxX + 1, maxY + 1)
            : new TextureRegion(0, 0, Width, Height);
    }

    private bool IsTransparentStrip(int x, int y, int width, int height)
    {
        var scaleX = Width / 64f;
        var scaleY = Height / 64f;
        var startX = Math.Clamp((int)MathF.Floor(x * scaleX), 0, Width);
        var startY = Math.Clamp((int)MathF.Floor(y * scaleY), 0, Height);
        var endX = Math.Clamp((int)MathF.Ceiling((x + width) * scaleX), startX, Width);
        var endY = Math.Clamp((int)MathF.Ceiling((y + height) * scaleY), startY, Height);

        for (var pixelY = startY; pixelY < endY; pixelY++)
        {
            var rowOffset = pixelY * Width;
            for (var pixelX = startX; pixelX < endX; pixelX++)
            {
                if ((Pixels[rowOffset + pixelX] >> 24) > 8)
                    return false;
            }
        }

        return true;
    }
}

internal readonly record struct MinecraftRenderSettings(
    double YawDegrees,
    double PitchDegrees,
    double Zoom,
    double VerticalOffset,
    bool IsSlim,
    double Bob,
    double ArmSwingDegrees,
    double AnimationPhase);

internal readonly record struct CapeTextureLayout(
    TextureRegion Outer,
    TextureRegion Inner,
    TextureRegion Left,
    TextureRegion Right,
    TextureRegion Top,
    TextureRegion Bottom)
{
    public static CapeTextureLayout Create(MinecraftTexture texture)
    {
        var atlasScaleX = texture.Width / 64f;
        var atlasScaleY = texture.Height / 32f;
        var looksLikeCapeAtlas =
            texture.Width >= 22 &&
            texture.Height >= 17 &&
            Math.Abs(texture.Width / (double)texture.Height - 2d) <= 0.22;

        if (looksLikeCapeAtlas)
        {
            return new CapeTextureLayout(
                new TextureRegion(12, 1, 22, 17).Scale(atlasScaleX, atlasScaleY),
                new TextureRegion(1, 1, 11, 17).Scale(atlasScaleX, atlasScaleY),
                new TextureRegion(0, 1, 1, 17).Scale(atlasScaleX, atlasScaleY),
                new TextureRegion(11, 1, 12, 17).Scale(atlasScaleX, atlasScaleY),
                new TextureRegion(1, 0, 11, 1).Scale(atlasScaleX, atlasScaleY),
                new TextureRegion(11, 0, 21, 1).Scale(atlasScaleX, atlasScaleY));
        }

        var opaqueBounds = texture.FindOpaqueBounds();
        var edgeWidth = Math.Min(1f, opaqueBounds.Width);
        var edgeHeight = Math.Min(1f, opaqueBounds.Height);
        return new CapeTextureLayout(
            opaqueBounds,
            opaqueBounds,
            new TextureRegion(
                opaqueBounds.Left,
                opaqueBounds.Top,
                opaqueBounds.Left + edgeWidth,
                opaqueBounds.Bottom),
            new TextureRegion(
                opaqueBounds.Right - edgeWidth,
                opaqueBounds.Top,
                opaqueBounds.Right,
                opaqueBounds.Bottom),
            new TextureRegion(
                opaqueBounds.Left,
                opaqueBounds.Top,
                opaqueBounds.Right,
                opaqueBounds.Top + edgeHeight),
            new TextureRegion(
                opaqueBounds.Left,
                opaqueBounds.Bottom - edgeHeight,
                opaqueBounds.Right,
                opaqueBounds.Bottom));
    }
}

internal readonly record struct TextureRegion(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;

    public float Height => Bottom - Top;

    public TextureRegion Scale(float x, float y) =>
        new(Left * x, Top * y, Right * x, Bottom * y);
}

internal sealed class MinecraftSkinRenderer
{
    private const int Supersample = 2;
    private const float CameraDistance = 72f;
    private const float ModelCenterY = 16f;
    private const float AlphaThreshold = 8f;

    private readonly object sync = new();
    private SKBitmap? frame;
    private int[] colorBuffer = [];
    private float[] depthBuffer = [];
    private int frameWidth;
    private int frameHeight;
    private MinecraftTexture? lastSkin;
    private MinecraftTexture? lastCape;
    private CapeTextureLayout? lastCapeLayout;
    private MinecraftRenderSettings lastSettings;
    private bool hasRenderedFrame;

    public void Draw(
        SKCanvas canvas,
        SKRect destination,
        MinecraftTexture skin,
        MinecraftTexture? cape,
        CapeTextureLayout? capeLayout,
        MinecraftRenderSettings settings)
    {
        if (destination.Width <= 0 || destination.Height <= 0)
            return;

        lock (sync)
        {
            var width = Math.Clamp((int)MathF.Ceiling(destination.Width * Supersample), 1, 1800);
            var height = Math.Clamp((int)MathF.Ceiling(destination.Height * Supersample), 1, 1800);
            EnsureFrame(width, height);
            var frameChanged =
                !hasRenderedFrame ||
                !ReferenceEquals(lastSkin, skin) ||
                !ReferenceEquals(lastCape, cape) ||
                lastCapeLayout != capeLayout ||
                lastSettings != settings;
            if (frameChanged)
            {
                Array.Clear(colorBuffer);
                Array.Fill(depthBuffer, float.NegativeInfinity);

                var state = new RenderState(
                    width,
                    height,
                    skin,
                    cape,
                    capeLayout,
                    settings,
                    colorBuffer,
                    depthBuffer);
                state.Render();

                Marshal.Copy(colorBuffer, 0, frame!.GetPixels(), colorBuffer.Length);
                lastSkin = skin;
                lastCape = cape;
                lastCapeLayout = capeLayout;
                lastSettings = settings;
                hasRenderedFrame = true;
            }

            using var frameImage = SKImage.FromBitmap(frame);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(
                frameImage,
                new SKRect(0, 0, width, height),
                destination,
                new SKSamplingOptions(SKCubicResampler.Mitchell),
                paint);
        }
    }

    public void ReleaseSurface()
    {
        lock (sync)
        {
            frame?.Dispose();
            frame = null;
            frameWidth = 0;
            frameHeight = 0;
            colorBuffer = [];
            depthBuffer = [];
            lastSkin = null;
            lastCape = null;
            lastCapeLayout = null;
            hasRenderedFrame = false;
        }
    }

    private void EnsureFrame(int width, int height)
    {
        if (frame is not null && frameWidth == width && frameHeight == height)
            return;

        frame?.Dispose();
        frame = new SKBitmap(
            new SKImageInfo(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
        frameWidth = width;
        frameHeight = height;
        colorBuffer = new int[width * height];
        depthBuffer = new float[width * height];
        hasRenderedFrame = false;
    }

    private sealed class RenderState
    {
        private readonly int width;
        private readonly int height;
        private readonly MinecraftTexture skin;
        private readonly MinecraftTexture? cape;
        private readonly CapeTextureLayout? capeLayout;
        private readonly MinecraftRenderSettings settings;
        private readonly int[] colors;
        private readonly float[] depths;
        private readonly float scale;
        private readonly float centerX;
        private readonly float centerY;
        private readonly float yawCosine;
        private readonly float yawSine;
        private readonly float pitchCosine;
        private readonly float pitchSine;

        public RenderState(
            int width,
            int height,
            MinecraftTexture skin,
            MinecraftTexture? cape,
            CapeTextureLayout? capeLayout,
            MinecraftRenderSettings settings,
            int[] colors,
            float[] depths)
        {
            this.width = width;
            this.height = height;
            this.skin = skin;
            this.cape = cape;
            this.capeLayout = capeLayout;
            this.settings = settings;
            this.colors = colors;
            this.depths = depths;

            scale = MathF.Min(width / 22f, height / 35f) * (float)settings.Zoom;
            centerX = width / 2f;
            var top = (height - 32f * scale) / 2f +
                      (float)settings.VerticalOffset * Supersample -
                      (float)settings.Bob * scale;
            centerY = top + ModelCenterY * scale;

            var yaw = DegreesToRadians(settings.YawDegrees);
            var pitch = DegreesToRadians(settings.PitchDegrees);
            yawCosine = MathF.Cos(yaw);
            yawSine = MathF.Sin(yaw);
            pitchCosine = MathF.Cos(pitch);
            pitchSine = MathF.Sin(pitch);
        }

        public void Render()
        {
            RenderPlayer();
            if (cape is not null && capeLayout is not null)
                RenderCape(cape, capeLayout.Value);
        }

        private void RenderPlayer()
        {
            RenderCuboid(
                new Cuboid(-4, 4, 0, 8, -4, 4),
                HeadBase,
                HeadOverlay,
                0.5f,
                null);

            RenderCuboid(
                new Cuboid(-4, 4, 8, 20, -2, 2),
                BodyBase,
                BodyOverlay,
                0.25f,
                null);

            var armWidth = settings.IsSlim ? 3f : 4f;
            var rightArmTransform = CreateLimbTransform(
                -(float)settings.ArmSwingDegrees,
                8,
                0);
            var leftArmTransform = CreateLimbTransform(
                (float)settings.ArmSwingDegrees,
                8,
                0);

            RenderCuboid(
                new Cuboid(-4 - armWidth, -4, 8, 20, -2, 2),
                settings.IsSlim ? RightArmSlimBase : RightArmBase,
                settings.IsSlim ? RightArmSlimOverlay : RightArmOverlay,
                0.25f,
                rightArmTransform);

            RenderCuboid(
                new Cuboid(4, 4 + armWidth, 8, 20, -2, 2),
                settings.IsSlim ? LeftArmSlimBase : LeftArmBase,
                settings.IsSlim ? LeftArmSlimOverlay : LeftArmOverlay,
                0.25f,
                leftArmTransform);

            var legSwing = (float)settings.ArmSwingDegrees * 0.56f;
            var rightLegTransform = CreateLimbTransform(legSwing, 20, 0);
            var leftLegTransform = CreateLimbTransform(-legSwing, 20, 0);

            RenderCuboid(
                new Cuboid(-4, 0, 20, 32, -2, 2),
                RightLegBase,
                RightLegOverlay,
                0.25f,
                rightLegTransform);

            RenderCuboid(
                new Cuboid(0, 4, 20, 32, -2, 2),
                LeftLegBase,
                LeftLegOverlay,
                0.25f,
                leftLegTransform);
        }

        private void RenderCape(MinecraftTexture capeTexture, CapeTextureLayout textures)
        {
            var flutter = settings.AnimationPhase == 0
                ? 0
                : Math.Sin(settings.AnimationPhase * 0.72) * 1.25;
            var angle = DegreesToRadians(-10.8 - flutter);
            var cosine = MathF.Cos(angle);
            var sine = MathF.Sin(angle);

            Vector3 TransformCape(Vector3 point)
            {
                var transformedY = point.Y * cosine - point.Z * sine;
                var transformedZ = point.Y * sine + point.Z * cosine;
                return new Vector3(point.X, 8 + transformedY, -2.08f + transformedZ);
            }

            var capeFaces = new FaceTextures(
                textures.Inner,
                textures.Outer,
                textures.Left,
                textures.Right,
                textures.Top,
                textures.Bottom);

            RenderFaces(
                capeTexture,
                new Cuboid(-5, 5, 0, 16, -0.96f, 0),
                capeFaces,
                TransformCape,
                0.98f);
        }

        private void RenderCuboid(
            Cuboid cuboid,
            FaceTextures baseTextures,
            FaceTextures overlayTextures,
            float overlayExpansion,
            Func<Vector3, Vector3>? localTransform)
        {
            RenderFaces(skin, cuboid, baseTextures, localTransform, 1f);
            RenderFaces(
                skin,
                cuboid.Expand(overlayExpansion),
                overlayTextures,
                localTransform,
                1f);
        }

        private void RenderFaces(
            MinecraftTexture texture,
            Cuboid cuboid,
            FaceTextures textures,
            Func<Vector3, Vector3>? localTransform,
            float materialBrightness)
        {
            RenderQuad(
                texture,
                ScaleRegion(textures.Front, texture),
                new Vector3(cuboid.X1, cuboid.Y1, cuboid.Z2),
                new Vector3(cuboid.X2, cuboid.Y1, cuboid.Z2),
                new Vector3(cuboid.X2, cuboid.Y2, cuboid.Z2),
                new Vector3(cuboid.X1, cuboid.Y2, cuboid.Z2),
                localTransform,
                materialBrightness);

            RenderQuad(
                texture,
                ScaleRegion(textures.Back, texture),
                new Vector3(cuboid.X2, cuboid.Y1, cuboid.Z1),
                new Vector3(cuboid.X1, cuboid.Y1, cuboid.Z1),
                new Vector3(cuboid.X1, cuboid.Y2, cuboid.Z1),
                new Vector3(cuboid.X2, cuboid.Y2, cuboid.Z1),
                localTransform,
                materialBrightness);

            RenderQuad(
                texture,
                ScaleRegion(textures.Right, texture),
                new Vector3(cuboid.X1, cuboid.Y1, cuboid.Z1),
                new Vector3(cuboid.X1, cuboid.Y1, cuboid.Z2),
                new Vector3(cuboid.X1, cuboid.Y2, cuboid.Z2),
                new Vector3(cuboid.X1, cuboid.Y2, cuboid.Z1),
                localTransform,
                materialBrightness);

            RenderQuad(
                texture,
                ScaleRegion(textures.Left, texture),
                new Vector3(cuboid.X2, cuboid.Y1, cuboid.Z2),
                new Vector3(cuboid.X2, cuboid.Y1, cuboid.Z1),
                new Vector3(cuboid.X2, cuboid.Y2, cuboid.Z1),
                new Vector3(cuboid.X2, cuboid.Y2, cuboid.Z2),
                localTransform,
                materialBrightness);

            RenderQuad(
                texture,
                ScaleRegion(textures.Top, texture),
                new Vector3(cuboid.X1, cuboid.Y1, cuboid.Z1),
                new Vector3(cuboid.X2, cuboid.Y1, cuboid.Z1),
                new Vector3(cuboid.X2, cuboid.Y1, cuboid.Z2),
                new Vector3(cuboid.X1, cuboid.Y1, cuboid.Z2),
                localTransform,
                materialBrightness);

            RenderQuad(
                texture,
                ScaleRegion(textures.Bottom, texture),
                new Vector3(cuboid.X1, cuboid.Y2, cuboid.Z2),
                new Vector3(cuboid.X2, cuboid.Y2, cuboid.Z2),
                new Vector3(cuboid.X2, cuboid.Y2, cuboid.Z1),
                new Vector3(cuboid.X1, cuboid.Y2, cuboid.Z1),
                localTransform,
                materialBrightness);
        }

        private void RenderQuad(
            MinecraftTexture texture,
            TextureRegion region,
            Vector3 model0,
            Vector3 model1,
            Vector3 model2,
            Vector3 model3,
            Func<Vector3, Vector3>? localTransform,
            float materialBrightness)
        {
            if (region.Width <= 0 || region.Height <= 0)
                return;

            var view0 = ToViewSpace(localTransform?.Invoke(model0) ?? model0);
            var view1 = ToViewSpace(localTransform?.Invoke(model1) ?? model1);
            var view2 = ToViewSpace(localTransform?.Invoke(model2) ?? model2);
            var view3 = ToViewSpace(localTransform?.Invoke(model3) ?? model3);
            var projected0 = Project(view0);
            var projected1 = Project(view1);
            var projected2 = Project(view2);
            var projected3 = Project(view3);

            var signedArea =
                Edge(projected0, projected1, projected2) +
                Edge(projected0, projected2, projected3);
            if (signedArea <= 0.001f)
                return;

            var edge1 = new Vector3(
                view1.X - view0.X,
                view1.Y - view0.Y,
                view1.Z - view0.Z);
            var edge2 = new Vector3(
                view3.X - view0.X,
                view3.Y - view0.Y,
                view3.Z - view0.Z);
            var normal = Vector3.Cross(edge1, edge2).Normalize();
            var light = new Vector3(0.28f, -0.42f, 0.86f);
            var diffuse = MathF.Max(0, Vector3.Dot(normal, light));
            var brightness = Math.Clamp((0.68f + diffuse * 0.32f) * materialBrightness, 0.56f, 1f);

            var uv0 = new TexturePoint(region.Left, region.Top);
            var uv1 = new TexturePoint(region.Right, region.Top);
            var uv2 = new TexturePoint(region.Right, region.Bottom);
            var uv3 = new TexturePoint(region.Left, region.Bottom);

            RasterizeTriangle(texture, region, projected0, projected1, projected2, uv0, uv1, uv2, brightness);
            RasterizeTriangle(texture, region, projected0, projected2, projected3, uv0, uv2, uv3, brightness);
        }

        private void RasterizeTriangle(
            MinecraftTexture texture,
            TextureRegion region,
            ProjectedVertex vertex0,
            ProjectedVertex vertex1,
            ProjectedVertex vertex2,
            TexturePoint uv0,
            TexturePoint uv1,
            TexturePoint uv2,
            float brightness)
        {
            var area = Edge(vertex0, vertex1, vertex2);
            if (area <= 0.0001f)
                return;

            var minX = Math.Clamp(
                (int)MathF.Floor(MathF.Min(vertex0.X, MathF.Min(vertex1.X, vertex2.X))),
                0,
                width - 1);
            var maxX = Math.Clamp(
                (int)MathF.Ceiling(MathF.Max(vertex0.X, MathF.Max(vertex1.X, vertex2.X))),
                0,
                width - 1);
            var minY = Math.Clamp(
                (int)MathF.Floor(MathF.Min(vertex0.Y, MathF.Min(vertex1.Y, vertex2.Y))),
                0,
                height - 1);
            var maxY = Math.Clamp(
                (int)MathF.Ceiling(MathF.Max(vertex0.Y, MathF.Max(vertex1.Y, vertex2.Y))),
                0,
                height - 1);

            var inverseArea = 1f / area;
            var u0OverDepth = uv0.U * vertex0.InverseDepth;
            var v0OverDepth = uv0.V * vertex0.InverseDepth;
            var u1OverDepth = uv1.U * vertex1.InverseDepth;
            var v1OverDepth = uv1.V * vertex1.InverseDepth;
            var u2OverDepth = uv2.U * vertex2.InverseDepth;
            var v2OverDepth = uv2.V * vertex2.InverseDepth;
            var sampler = new TextureSampler(texture, region);

            for (var y = minY; y <= maxY; y++)
            {
                var sampleY = y + 0.5f;
                var rowOffset = y * width;
                for (var x = minX; x <= maxX; x++)
                {
                    var sampleX = x + 0.5f;
                    var weight0 = Edge(vertex1, vertex2, sampleX, sampleY) * inverseArea;
                    var weight1 = Edge(vertex2, vertex0, sampleX, sampleY) * inverseArea;
                    var weight2 = 1f - weight0 - weight1;
                    if (weight0 < -0.0001f || weight1 < -0.0001f || weight2 < -0.0001f)
                        continue;

                    var inverseDepth =
                        weight0 * vertex0.InverseDepth +
                        weight1 * vertex1.InverseDepth +
                        weight2 * vertex2.InverseDepth;
                    var pixelIndex = rowOffset + x;
                    if (inverseDepth <= depths[pixelIndex] + 0.0000001f)
                        continue;

                    var u =
                        (weight0 * u0OverDepth +
                         weight1 * u1OverDepth +
                         weight2 * u2OverDepth) / inverseDepth;
                    var v =
                        (weight0 * v0OverDepth +
                         weight1 * v1OverDepth +
                         weight2 * v2OverDepth) / inverseDepth;
                    var sourceColor = sampler.Sample(u, v);
                    var alpha = (int)(sourceColor >> 24);
                    if (alpha < AlphaThreshold)
                        continue;

                    colors[pixelIndex] = BlendPremultiplied(
                        colors[pixelIndex],
                        sourceColor,
                        brightness);
                    depths[pixelIndex] = inverseDepth;
                }
            }
        }

        private ViewVertex ToViewSpace(Vector3 point)
        {
            var x = point.X * yawCosine + point.Z * yawSine;
            var yawDepth = -point.X * yawSine + point.Z * yawCosine;
            var centeredY = point.Y - ModelCenterY;
            var y = centeredY * pitchCosine - yawDepth * pitchSine;
            var z = centeredY * pitchSine + yawDepth * pitchCosine;
            return new ViewVertex(x, y, z);
        }

        private ProjectedVertex Project(ViewVertex vertex)
        {
            var cameraDepth = MathF.Max(12f, CameraDistance - vertex.Z);
            var inverseDepth = 1f / cameraDepth;
            var perspectiveScale = CameraDistance * inverseDepth;
            return new ProjectedVertex(
                centerX + vertex.X * scale * perspectiveScale,
                centerY + vertex.Y * scale * perspectiveScale,
                inverseDepth);
        }

        private static Func<Vector3, Vector3> CreateLimbTransform(
            float angleDegrees,
            float pivotY,
            float pivotZ)
        {
            var angle = DegreesToRadians(angleDegrees);
            var cosine = MathF.Cos(angle);
            var sine = MathF.Sin(angle);

            return point =>
            {
                var y = point.Y - pivotY;
                var z = point.Z - pivotZ;
                return new Vector3(
                    point.X,
                    pivotY + y * cosine - z * sine,
                    pivotZ + y * sine + z * cosine);
            };
        }

        private TextureRegion ScaleRegion(TextureRegion region, MinecraftTexture texture)
        {
            if (ReferenceEquals(texture, skin))
                return region.Scale(texture.Width / 64f, texture.Height / 64f);

            return region;
        }

        private static int BlendPremultiplied(int destination, uint source, float brightness)
        {
            var sourceAlpha = (int)(source >> 24);
            var sourceRed = Math.Clamp(
                (int)(((source >> 16) & 0xff) * brightness + 0.5f),
                0,
                255);
            var sourceGreen = Math.Clamp(
                (int)(((source >> 8) & 0xff) * brightness + 0.5f),
                0,
                255);
            var sourceBlue = Math.Clamp(
                (int)((source & 0xff) * brightness + 0.5f),
                0,
                255);

            if (sourceAlpha >= 255)
            {
                return unchecked((int)(
                    0xff000000u |
                    (uint)(sourceRed << 16) |
                    (uint)(sourceGreen << 8) |
                    (uint)sourceBlue));
            }

            var sourcePremultipliedRed = (sourceRed * sourceAlpha + 127) / 255;
            var sourcePremultipliedGreen = (sourceGreen * sourceAlpha + 127) / 255;
            var sourcePremultipliedBlue = (sourceBlue * sourceAlpha + 127) / 255;
            var inverseAlpha = 255 - sourceAlpha;

            var destinationAlpha = (destination >> 24) & 0xff;
            var destinationRed = (destination >> 16) & 0xff;
            var destinationGreen = (destination >> 8) & 0xff;
            var destinationBlue = destination & 0xff;

            var outputAlpha = sourceAlpha + (destinationAlpha * inverseAlpha + 127) / 255;
            var outputRed = sourcePremultipliedRed + (destinationRed * inverseAlpha + 127) / 255;
            var outputGreen = sourcePremultipliedGreen + (destinationGreen * inverseAlpha + 127) / 255;
            var outputBlue = sourcePremultipliedBlue + (destinationBlue * inverseAlpha + 127) / 255;

            return
                (outputAlpha << 24) |
                (outputRed << 16) |
                (outputGreen << 8) |
                outputBlue;
        }

        private static float Edge(ProjectedVertex a, ProjectedVertex b, ProjectedVertex point) =>
            Edge(a, b, point.X, point.Y);

        private static float Edge(ProjectedVertex a, ProjectedVertex b, float x, float y) =>
            (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

        private readonly struct TextureSampler
        {
            private readonly uint[] pixels;
            private readonly int textureWidth;
            private readonly int minX;
            private readonly int minY;
            private readonly int maxX;
            private readonly int maxY;

            public TextureSampler(MinecraftTexture texture, TextureRegion region)
            {
                pixels = texture.Pixels;
                textureWidth = texture.Width;
                minX = Math.Clamp((int)MathF.Floor(region.Left), 0, texture.Width - 1);
                minY = Math.Clamp((int)MathF.Floor(region.Top), 0, texture.Height - 1);
                maxX = Math.Clamp(
                    (int)MathF.Ceiling(region.Right) - 1,
                    minX,
                    texture.Width - 1);
                maxY = Math.Clamp(
                    (int)MathF.Ceiling(region.Bottom) - 1,
                    minY,
                    texture.Height - 1);
            }

            public uint Sample(float u, float v)
            {
                var x = (int)u;
                var y = (int)v;
                if (x < minX)
                    x = minX;
                else if (x > maxX)
                    x = maxX;

                if (y < minY)
                    y = minY;
                else if (y > maxY)
                    y = maxY;

                return pixels[y * textureWidth + x];
            }
        }
    }

    private static float DegreesToRadians(double degrees) =>
        (float)(degrees * Math.PI / 180d);

    private static readonly FaceTextures HeadBase = CreateFaceTextures(0, 0, 8, 8, 8);
    private static readonly FaceTextures HeadOverlay = CreateFaceTextures(32, 0, 8, 8, 8);
    private static readonly FaceTextures BodyBase = CreateFaceTextures(16, 16, 8, 12, 4);
    private static readonly FaceTextures BodyOverlay = CreateFaceTextures(16, 32, 8, 12, 4);
    private static readonly FaceTextures RightArmBase = CreateFaceTextures(40, 16, 4, 12, 4);
    private static readonly FaceTextures RightArmOverlay = CreateFaceTextures(40, 32, 4, 12, 4);
    private static readonly FaceTextures RightArmSlimBase = CreateFaceTextures(40, 16, 3, 12, 4);
    private static readonly FaceTextures RightArmSlimOverlay = CreateFaceTextures(40, 32, 3, 12, 4);
    private static readonly FaceTextures LeftArmBase = CreateFaceTextures(32, 48, 4, 12, 4);
    private static readonly FaceTextures LeftArmOverlay = CreateFaceTextures(48, 48, 4, 12, 4);
    private static readonly FaceTextures LeftArmSlimBase = CreateFaceTextures(32, 48, 3, 12, 4);
    private static readonly FaceTextures LeftArmSlimOverlay = CreateFaceTextures(48, 48, 3, 12, 4);
    private static readonly FaceTextures RightLegBase = CreateFaceTextures(0, 16, 4, 12, 4);
    private static readonly FaceTextures RightLegOverlay = CreateFaceTextures(0, 32, 4, 12, 4);
    private static readonly FaceTextures LeftLegBase = CreateFaceTextures(16, 48, 4, 12, 4);
    private static readonly FaceTextures LeftLegOverlay = CreateFaceTextures(0, 48, 4, 12, 4);

    private static FaceTextures CreateFaceTextures(
        float u,
        float v,
        float partWidth,
        float partHeight,
        float partDepth)
    {
        return new FaceTextures(
            new TextureRegion(
                u + partDepth,
                v + partDepth,
                u + partWidth + partDepth,
                v + partDepth + partHeight),
            new TextureRegion(
                u + partWidth + partDepth * 2,
                v + partDepth,
                u + partWidth * 2 + partDepth * 2,
                v + partDepth + partHeight),
            new TextureRegion(
                u,
                v + partDepth,
                u + partDepth,
                v + partDepth + partHeight),
            new TextureRegion(
                u + partWidth + partDepth,
                v + partDepth,
                u + partWidth + partDepth * 2,
                v + partDepth + partHeight),
            new TextureRegion(
                u + partDepth,
                v,
                u + partWidth + partDepth,
                v + partDepth),
            new TextureRegion(
                u + partWidth + partDepth,
                v,
                u + partWidth * 2 + partDepth,
                v + partDepth));
    }

    private readonly record struct FaceTextures(
        TextureRegion Front,
        TextureRegion Back,
        TextureRegion Right,
        TextureRegion Left,
        TextureRegion Top,
        TextureRegion Bottom);

    private readonly record struct Cuboid(
        float X1,
        float X2,
        float Y1,
        float Y2,
        float Z1,
        float Z2)
    {
        public Cuboid Expand(float amount) =>
            new(
                X1 - amount,
                X2 + amount,
                Y1 - amount,
                Y2 + amount,
                Z1 - amount,
                Z2 + amount);
    }

    private readonly record struct TexturePoint(float U, float V);

    private readonly record struct ProjectedVertex(float X, float Y, float InverseDepth);

    private readonly record struct ViewVertex(float X, float Y, float Z);

    private readonly record struct Vector3(float X, float Y, float Z)
    {
        public static Vector3 Cross(Vector3 left, Vector3 right) =>
            new(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);

        public static float Dot(Vector3 left, Vector3 right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;

        public Vector3 Normalize()
        {
            var length = MathF.Sqrt(X * X + Y * Y + Z * Z);
            return length <= 0.00001f
                ? new Vector3(0, 0, 1)
                : new Vector3(X / length, Y / length, Z / length);
        }
    }
}
