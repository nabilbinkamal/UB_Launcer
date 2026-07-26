using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Launcher.UI.Controls;

public sealed class SkinPreviewControl : Control
{
    private const double FrontPreviewYaw = -18;
    private const double CapePreviewYaw = 150;

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<SkinPreviewControl, string?>(nameof(Source));

    public static readonly StyledProperty<string?> CapeSourceProperty =
        AvaloniaProperty.Register<SkinPreviewControl, string?>(nameof(CapeSource));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<SkinPreviewControl, double>(nameof(Zoom), 1d);

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<SkinPreviewControl, double>(nameof(VerticalOffset));

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<SkinPreviewControl, bool>(nameof(IsAnimated));

    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<SkinPreviewControl, bool>(nameof(IsInteractive));

    public static readonly StyledProperty<bool> IsSlimProperty =
        AvaloniaProperty.Register<SkinPreviewControl, bool>(nameof(IsSlim));

    private readonly DispatcherTimer animationTimer;
    private readonly MinecraftSkinRenderer renderer = new();
    private MinecraftTexture? skin;
    private MinecraftTexture? cape;
    private CapeTextureLayout? capeLayout;
    private bool detectedSlim;
    private double animationPhase;
    private double yawDegrees = FrontPreviewYaw;
    private bool isDragging;
    private Point lastPointerPosition;

    public SkinPreviewControl()
    {
        animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        animationTimer.Tick += OnAnimationTick;
    }

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? CapeSource
    {
        get => GetValue(CapeSourceProperty);
        set => SetValue(CapeSourceProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public bool IsSlim
    {
        get => GetValue(IsSlimProperty);
        set => SetValue(IsSlimProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
            LoadSkin();

        if (change.Property == CapeSourceProperty)
            LoadCape();

        if (change.Property == IsAnimatedProperty)
            UpdateAnimationState();

        if (change.Property == SourceProperty ||
            change.Property == CapeSourceProperty ||
            change.Property == ZoomProperty ||
            change.Property == VerticalOffsetProperty ||
            change.Property == IsSlimProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateAnimationState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        animationTimer.Stop();
        renderer.ReleaseSurface();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsInteractive || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        isDragging = true;
        lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isDragging)
            return;

        var currentPosition = e.GetPosition(this);
        yawDegrees = NormalizeDegrees(
            yawDegrees + (currentPosition.X - lastPointerPosition.X) * 0.55);
        lastPointerPosition = currentPosition;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!isDragging)
            return;

        isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (skin is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var idleYaw = IsAnimated && !isDragging
            ? Math.Sin(animationPhase * 0.45) * 2.4
            : 0;
        var bob = IsAnimated && !isDragging
            ? Math.Abs(Math.Sin(animationPhase)) * 0.18
            : 0;
        var armSwing = IsAnimated && !isDragging
            ? Math.Sin(animationPhase) * 3.4
            : 0;
        var pitch = cape is null ? -5.5 : -8.5;

        context.Custom(new SkinDrawOperation(
            new Rect(Bounds.Size),
            renderer,
            skin,
            cape,
            capeLayout,
            new MinecraftRenderSettings(
                yawDegrees + idleYaw,
                pitch,
                Zoom,
                VerticalOffset,
                IsSlim || detectedSlim,
                bob,
                armSwing,
                IsAnimated ? animationPhase : 0)));
    }

    public static bool DetectSlimModel(string source)
    {
        var texture = LoadTexture(source);
        return texture?.DetectSlimModel() == true;
    }

    private void LoadSkin()
    {
        skin = LoadTexture(Source);
        detectedSlim = skin?.DetectSlimModel() == true;
    }

    private void LoadCape()
    {
        cape = LoadTexture(CapeSource);
        capeLayout = cape is null ? null : CapeTextureLayout.Create(cape);
        yawDegrees = cape is null ? FrontPreviewYaw : CapePreviewYaw;
    }

    private static MinecraftTexture? LoadTexture(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        try
        {
            using var stream = source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? AssetLoader.Open(new Uri(source))
                : File.OpenRead(source);
            return MinecraftTexture.Decode(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void UpdateAnimationState()
    {
        if (IsAnimated && VisualRoot is not null)
            animationTimer.Start();
        else
            animationTimer.Stop();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        animationPhase += 0.08;
        if (animationPhase > Math.PI * 2)
            animationPhase -= Math.PI * 2;
        InvalidateVisual();
    }

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private sealed class SkinDrawOperation(
        Rect bounds,
        MinecraftSkinRenderer renderer,
        MinecraftTexture skin,
        MinecraftTexture? cape,
        CapeTextureLayout? capeLayout,
        MinecraftRenderSettings settings) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
                return;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var width = (float)Bounds.Width;
            var height = (float)Bounds.Height;
            var saveCount = canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, width, height), SKClipOperation.Intersect, true);

            try
            {
                DrawShadow(canvas, width, height);
                renderer.Draw(
                    canvas,
                    new SKRect(0, 0, width, height),
                    skin,
                    cape,
                    capeLayout,
                    settings);
            }
            finally
            {
                canvas.RestoreToCount(saveCount);
            }
        }

        public bool HitTest(Point point) => Bounds.Contains(point);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        private void DrawShadow(SKCanvas canvas, float width, float height)
        {
            var scale = (float)(Math.Min(width / 22f, height / 35f) * settings.Zoom);
            var top = (height - 32f * scale) / 2f +
                      (float)settings.VerticalOffset -
                      (float)settings.Bob * scale;
            using var shadowPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 72),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f)
            };
            canvas.DrawOval(
                new SKRect(
                    width / 2f - 6.8f * scale,
                    top + 31.7f * scale,
                    width / 2f + 6.8f * scale,
                    top + 33.4f * scale),
                shadowPaint);
        }
    }
}
