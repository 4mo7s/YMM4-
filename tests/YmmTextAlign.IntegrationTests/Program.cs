using System.Collections.Immutable;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.Mathematics;
using YmmTextAlign;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.Settings;
using FrameTime = YukkuriMovieMaker.Player.Video.FrameTime;
using TextStyle = YukkuriMovieMaker.Project.Items.Style;
using Rect = System.Windows.Rect;

internal static class Program
{
    private static int checks;
    private static bool effectsOnly;
    [STAThread]
    private static void Main(string[] args)
    {
        effectsOnly = args.Contains("--effects-only");
        var directory = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? @"C:\YukkuriMovieMaker4_Lite");
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Run();
        Console.WriteLine($"PASS: {checks} assertions (YMM4 4.53.0.1 rendering, alignment, history, UI)");
    }

    private static void Run()
    {
        Check(typeof(TextItem).Assembly.GetName().Version == new Version(4, 53, 0, 1), "host version");
        var timeline = new Timeline();
        timeline.VideoInfo.Width = 1920;
        timeline.VideoInfo.Height = 1080;
        timeline.VideoInfo.FPS = 30;
        var scenes = new Scenes(true);
        var undo = new UndoRedoManager();
        undo.Subscribe(timeline);
        var vm = new AlignToolViewModel();
        vm.SetTimelineToolInfo(new TimelineToolInfo(timeline, scenes, undo, new AsyncAwaitStatus()));
        using var raster = new Raster(timeline, scenes);

        TextItem Select(string text = "ああああ\r\nええええ", BasePoint point = BasePoint.LeftTop, TextStyle style = TextStyle.Normal)
        {
            var item = new TextItem { Text = text, Font = "Yu Gothic", BasePoint = point, Style = style, Length = 90 };
            item.FontSize.AddToEachValues(120 - item.FontSize.GetValue(0, item.Length, 30));
            timeline.Items = ImmutableList.Create<IItem>(item);
            timeline.SelectedItems = timeline.Items;
            return item;
        }
        double Value(Animation a, TextItem item) => a.GetValue(timeline.CurrentFrame, item.Length, 30);
        void Place(TextItem item, int? column, int? row, bool pixels = true)
        {
            Check(vm.Align(column, row), vm.Status);
            if (pixels) raster.CheckPixels(item, column.HasValue, row.HasValue, vm.HorizontalMargin, vm.VerticalMargin);
        }

        if (!effectsOnly)
        {
        // Begin with one centered text, then exercise all top/bottom/center buttons.
        var single = Select("中央配置", BasePoint.RightBottom);
        Place(single, 1, 1);
        var central = raster.Bounds(single);
        Check(Math.Abs((central.Left + central.Right) / 2) <= 2 && Math.Abs((central.Top + central.Bottom) / 2) <= 2,
            "single text render bounds centered");

        var renderCases = 0;
        foreach (var anchor in new[] { false, true })
        foreach (var internalAlign in new[] { false, true })
        foreach (var point in new[] { BasePoint.LeftTop, BasePoint.CenterCenter, BasePoint.RightBottom })
        {
            var item = Select(point: point);
            vm.AnchorPointBasis = anchor;
            vm.ChangeTextAlignment = internalAlign;
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var originalPoint = item.BasePoint;
                Place(item, col, row);
                Check(!internalAlign ? item.BasePoint == originalPoint : VerticalPart(item.BasePoint) == VerticalPart(originalPoint),
                    "base-point setting preserved according to option");
                if (!anchor)
                {
                    var b = raster.Bounds(item);
                    var x = col == 0 ? b.Left + 880 : col == 1 ? (b.Left + b.Right) / 2 : b.Right - 880;
                    var y = row == 0 ? b.Top + 460 : row == 1 ? (b.Top + b.Bottom) / 2 : b.Bottom - 460;
                    Check(Math.Abs(x) <= 2 && Math.Abs(y) <= 2, "actual drawing edges match selected direction");
                }
                renderCases++;
            }
        }
        Console.WriteLine($"PASS: {renderCases} multiline placements; nine directions, three vertical base points, both toggles");

        vm.AnchorPointBasis = false;
        vm.ChangeTextAlignment = false;
        foreach (var style in Enum.GetValues<TextStyle>())
        {
            var item = Select("ああああ\r\nええええ\r\n3行目", BasePoint.RightBottom, style);
            item.LineHeight2.AddToEachValues(50);
            Place(item, 0, 0);
            Place(item, 2, 2);
        }
        foreach (var rotation in new[] { -30d, 30d, 90d })
        {
            var item = Select(style: TextStyle.Border);
            item.Zoom.AddToEachValues(30);
            item.Rotation.AddToEachValues(rotation);
            item.IsInverted = true;
            Place(item, 0, 0);
            Place(item, 2, 2);
        }
        var wrapped = Select("折り返した文字も画面内に入ります");
        wrapped.WordWrap = WordWrap.Character;
        wrapped.MaxWidth.AddToEachValues(700 - wrapped.MaxWidth.GetValue(0, 90, 30));
        wrapped.LetterSpacing2.AddToEachValues(4);
        wrapped.Underline = true;
        Place(wrapped, 2, 2);
        var vertical = Select("縦書き\r\n二列目", BasePoint.VTopRight);
        Place(vertical, 0, 0);
        Place(vertical, 2, 2);
        Console.WriteLine("PASS: all seven text styles, three lines, spacing, wrapping, rotation, zoom, inversion, vertical writing");

        // Reproduce the reported Center Point effect without removing or changing it.
        // Reflection is test-only because this built-in effect class is internal.
        foreach (var keepPosition in new[] { false, true })
        foreach (var mode in new[] { ("Center", "Center"), ("Left", "Top"), ("Right", "Bottom"), ("Custom", "Custom") })
        {
            var item = Select(point: BasePoint.RightBottom, style: TextStyle.Border);
            item.Zoom.AddToEachValues(25);
            item.Rotation.AddToEachValues(15);
            var effect = CenterEffect(mode.Item1, mode.Item2, keepPosition);
            item.VideoEffects = ImmutableList.Create(effect);
            var originalLabel = effect.Label;
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                Place(item, column, row);
                var b = raster.Bounds(item);
                var edgeX = column == 0 ? b.Left + 880 : column == 1 ? (b.Left + b.Right) / 2 : b.Right - 880;
                var edgeY = row == 0 ? b.Top + 460 : row == 1 ? (b.Top + b.Bottom) / 2 : b.Bottom - 460;
                Check(Math.Abs(edgeX) <= 2 && Math.Abs(edgeY) <= 2, "center effect included in bounds alignment");
                Check(item.BasePoint == BasePoint.RightBottom && item.VideoEffects.Single() == effect && effect.Label == originalLabel,
                    "center effect and base-point settings retained");
            }
            vm.AnchorPointBasis = true;
            Place(item, 0, 0);
            Place(item, 2, 2);
            vm.AnchorPointBasis = false;
        }
        Console.WriteLine("PASS: Center Point effect, 72 OFF placements + 16 ON placements; all pivot modes, keep-position, zoom, rotation");
        }
        vm.AnchorPointBasis = false;
        vm.ChangeTextAlignment = false;

        var effectCases = new List<IVideoEffect[]> {
            new[] { Effect("GaussianBlurEffect", ("Blur", 12)) },
            new[] { Effect("ShadowEffect", ("X", 65), ("Y", 40), ("Blur", 8)) },
            new[] { Effect("BorderBlurEffect", ("Blur", 8)) },
            new[] { Effect("MarginEffect", ("Left", 45), ("Right", 20), ("Top", 15), ("Bottom", 35)) },
            new[] { Effect("ZoomEffect", ("Zoom", 110), ("ZoomX", 105), ("ZoomY", 80)) },
            new[] { Effect("CropEffect", ("Left", 15), ("Top", 10)) },
            new[] { Effect("FillBackgroundEffect", ("Left", 15), ("Right", 20), ("Round", 10)) },
            new[] { Effect("RandomMoveEffect", ("X", 40), ("Y", 30)) },
            new[] { Effect("CameraPositionEffect", ("RotationZ", 25), ("X", 30)) },
            new[] { CenterEffect("Custom", "Custom", true), Effect("ShadowEffect", ("X", 45), ("Y", 35)),
                Effect("GaussianBlurEffect", ("Blur", 4)), Effect("MarginEffect", ("Right", 20)), Effect("ZoomEffect", ("Zoom", 105)) },
            new IVideoEffect[] { new TestPositionEffect { Mode = TestPositionMode.Coupled } },
        };
        foreach (var effects in effectCases)
        {
            var item = Select(point: BasePoint.LeftTop, style: TextStyle.Border);
            item.VideoEffects = ImmutableList.CreateRange(effects);
            var originalSettings = YukkuriMovieMaker.Json.Json.GetJsonText(item.VideoEffects, null);
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                Check(vm.Align(column, row), $"Effects {string.Join(",", effects.Select(e => e.GetType().Name))}, column={column}, row={row}: {vm.Status}");
                raster.CheckPixels(item, true, true, vm.HorizontalMargin, vm.VerticalMargin);
                var b = raster.Bounds(item);
                var edgeX = column == 0 ? b.Left + 880 : column == 1 ? (b.Left + b.Right) / 2 : b.Right - 880;
                var edgeY = row == 0 ? b.Top + 460 : row == 1 ? (b.Top + b.Bottom) / 2 : b.Bottom - 460;
                Check(Math.Abs(edgeX) <= 2 && Math.Abs(edgeY) <= 2, "video effects aligned using final drawing bounds");
                Check(YukkuriMovieMaker.Json.Json.GetJsonText(item.VideoEffects, null) == originalSettings && item.BasePoint == BasePoint.LeftTop,
                    "all video effect settings and base point retained");
            }
            var otherAxis = Value(item.Y, item);
            Place(item, 2, null);
            Check(Value(item.Y, item) == otherAxis, "video-effect X-only preserves Y parameter");
            vm.AnchorPointBasis = true;
            Place(item, 0, 0); Place(item, 2, 2);
            vm.AnchorPointBasis = false;
            Console.WriteLine("PASS: effect chain " + string.Join(", ", effects.Select(e => e.GetType().Name)));
        }
        var fixedPosition = Select();
        var fixedEffect = new TestPositionEffect { Mode = TestPositionMode.Fixed };
        fixedPosition.VideoEffects = ImmutableList.Create<IVideoEffect>(fixedEffect);
        var fixedSettings = YukkuriMovieMaker.Json.Json.GetJsonText(fixedPosition.VideoEffects, null);
        Check(!vm.Align(2, 2) && Value(fixedPosition.X, fixedPosition) == 0 && Value(fixedPosition.Y, fixedPosition) == 0,
            "effect that ignores item position fails without changing item");
        Check(YukkuriMovieMaker.Json.Json.GetJsonText(fixedPosition.VideoEffects, null) == fixedSettings, "failed solve preserves effect settings");
        fixedEffect.IsEnabled = false;
        Place(fixedPosition, 2, 2);
        Console.WriteLine("PASS: disabled effects and non-movable effect handling");
        var depth = Select(point: BasePoint.CenterCenter);
        depth.Z.AddToEachValues(100);
        Place(depth, 0, 0); Place(depth, 2, 2);
        Console.WriteLine("PASS: Z-position projection");
        if (effectsOnly) return;

        var axis = Select(point: BasePoint.CenterCenter);
        axis.X.AddToEachValues(37);
        axis.Y.AddToEachValues(-22);
        var oldY = Value(axis.Y, axis);
        Place(axis, 2, null);
        Check(Value(axis.Y, axis) == oldY, "X-only preserves Y");
        var oldX = Value(axis.X, axis);
        Place(axis, null, 2);
        Check(Value(axis.X, axis) == oldX, "Y-only preserves X");

        var first = Select("複数選択\r\n1つ目", BasePoint.LeftTop, TextStyle.Border);
        var second = (TextItem)first.GetClone();
        second.Text = "二つ目";
        second.BasePoint = BasePoint.RightBottom;
        var unsupported = (TextItem)first.GetClone();
        unsupported.DisplayInterval = 0.1;
        var locked = (TextItem)first.GetClone();
        locked.IsLocked = true;
        timeline.Items = ImmutableList.Create<IItem>(first, second, unsupported, locked);
        timeline.SelectedItems = timeline.Items;
        undo.Record();
        undo.Clear();
        undo.Subscribe(timeline); // Clear also removes subscriptions in YMM4 4.53.0.1.
        Place(first, 2, 2);
        raster.CheckPixels(second, true, true, 80, 80);
        Check(Value(unsupported.X, unsupported) == 0 && Value(locked.X, locked) == 0, "unsupported and locked unchanged");
        var alignedX = Value(first.X, first);
        undo.UndoAsync().GetAwaiter().GetResult();
        Check(Value(first.X, first) == 0 && Value(second.Y, second) == 0, "one undo restores batch");
        undo.RedoAsync().GetAwaiter().GetResult();
        Check(Value(first.X, first) == alignedX, "redo restores batch");

        var animated = Select(point: BasePoint.CenterCenter);
        animated.X.BeginEdit();
        animated.X.AnimationType = AnimationType.直線移動;
        animated.X.EndEditAsync().GetAwaiter().GetResult();
        animated.X.Values[0].Value = 20;
        animated.X.Values[1].Value = 120;
        var delta = animated.X.GetValue(89, 90, 30) - animated.X.GetValue(0, 90, 30);
        timeline.CurrentFrame = 40;
        Place(animated, 1, 1);
        Check(Math.Abs(animated.X.GetValue(89, 90, 30) - animated.X.GetValue(0, 90, 30) - delta) < 0.001,
            "animation shape preserved");
        timeline.CurrentFrame = 0;

        var oversized = Select(new string('W', 100));
        Check(!vm.Align(0, 0) && Value(oversized.X, oversized) == 0, "oversize rejected without movement");
        Check(!vm.TrySetMargins("NaN", "0"), "invalid margin rejected");
        vm.HorizontalMargin = 80;
        vm.VerticalMargin = 80;
        Console.WriteLine("PASS: axes, mixed selection, locked/unsupported cases, undo/redo, animation, oversize handling");

        var preview = Select(style: TextStyle.Border);
        Place(preview, 2, 2);
        Directory.CreateDirectory("work/rebuild-verification");
        raster.Save(preview, "work/rebuild-verification/multiline-bottom-right.png");
        var view = new AlignToolView { DataContext = vm };
        foreach (var width in new[] { 160d, 180d, 240d, 480d })
        {
            view.Measure(new System.Windows.Size(width, 360));
            view.Arrange(new Rect(0, 0, width, 360));
            view.UpdateLayout();
            var grid = (UniformGrid)view.FindName("PlacementGrid");
            Check(grid.Children.Count == 9, "nine placement buttons");
            foreach (Button button in grid.Children)
            {
                var rect = button.TransformToAncestor(view).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
                Check(button.Content is null && button.ActualHeight >= 46 && button.ActualWidth >= 35 && rect.Right <= width,
                    $"blank button remains usable at width {width}");
            }
            if (width == 180) SaveUi(view, "work/rebuild-verification/panel.png", 180, 360);
        }
        var clickTarget = (Button)((UniformGrid)view.FindName("PlacementGrid")).Children[0];
        clickTarget.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, clickTarget));
        Check(vm.Status.Contains("整列しました"), "UI click reaches alignment");
        raster.CheckPixels(preview, true, true, 80, 80);
        Console.WriteLine("PASS: responsive WPF panel and click handler");
    }

    private static string VerticalPart(BasePoint point) => point.ToString().EndsWith("Top") ? "Top" : point.ToString().EndsWith("Bottom") ? "Bottom" : "Center";
    private static IVideoEffect CenterEffect(string horizontal, string vertical, bool keepPosition)
    {
        var type = typeof(TextItem).Assembly.GetType("YukkuriMovieMaker.Project.Effects.CenterPointEffect", throwOnError: true)!;
        var effect = (IVideoEffect)Activator.CreateInstance(type)!;
        type.GetProperty("Horizontal")!.SetValue(effect, Enum.Parse(type.GetProperty("Horizontal")!.PropertyType, horizontal));
        type.GetProperty("Vertical")!.SetValue(effect, Enum.Parse(type.GetProperty("Vertical")!.PropertyType, vertical));
        type.GetProperty("IsKeepPosition")!.SetValue(effect, keepPosition);
        ((Animation)type.GetProperty("X")!.GetValue(effect)!).AddToEachValues(75);
        ((Animation)type.GetProperty("Y")!.GetValue(effect)!).AddToEachValues(-60);
        return effect;
    }
    private static IVideoEffect Effect(string name, params (string Property, double Value)[] values)
    {
        var type = typeof(TextItem).Assembly.GetType("YukkuriMovieMaker.Project.Effects." + name, true)!;
        var effect = (IVideoEffect)Activator.CreateInstance(type)!;
        foreach (var value in values)
        {
            var animation = (Animation)type.GetProperty(value.Property)!.GetValue(effect)!;
            animation.AddToEachValues(value.Value - animation.GetValue(0, 90, 30));
        }
        return effect;
    }
    internal static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("FAIL: " + message); }
    private static void SaveUi(FrameworkElement view, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        SavePng(bitmap, path);
    }
    internal static void SavePng(BitmapSource bitmap, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}

// Independent rendering of the final host item, including its actual X/Y.
// A canvas larger than the project catches pixels that would be clipped by export.
internal sealed class Raster : IDisposable
{
    private const int Padding = 256;
    private readonly Timeline timeline;
    private readonly Scene scene;
    private readonly GraphicsDevices devices = new();
    private readonly IGraphicsDevicesAndContext context;
    internal Raster(Timeline timeline, Scenes scenes)
    {
        this.timeline = timeline;
        scene = new Scene(timeline, scenes, Array.Empty<Guid>());
        context = devices.CreateContext();
    }
    private RenderedSample Render(TextItem item)
    {
        var fps = timeline.VideoInfo.FPS;
        var frame = timeline.CurrentFrame - item.Frame;
        var desc = new TimelineItemSourceDescription(new TimelineSourceDescription(
            new System.Drawing.Size(timeline.VideoInfo.Width, timeline.VideoInfo.Height),
            new FrameTime(timeline.CurrentFrame, fps), new FrameTime(item.Frame + item.Length, fps), fps,
            TimelineSourceUsage.Paused, scene.ID, scene.AllScenesWithParentScenesInfo), frame, item.Length, item.Layer);
        var source = item.CreateVideoSource(devices, scene);
        var sample = new RenderedSample(source);
        source.Update(desc);
        for (var i = 0; i < source.Outputs.Count; i++)
        {
            var output = source.Outputs[i];
            var drawing = new DrawDescription(new Vector3((float)item.X.GetValue(frame,item.Length,fps),
                (float)item.Y.GetValue(frame,item.Length,fps),(float)item.Z.GetValue(frame,item.Length,fps)) + new Vector3(output.DrawingOffset,0), output.Center,
                new Vector2((float)item.Zoom.GetValue(frame,item.Length,fps)/100),
                new Vector3(0,0,(float)item.Rotation.GetValue(frame,item.Length,fps)), Matrix4x4.Identity,
                (Vortice.Direct2D1.InterpolationMode)SettingsBase<YMMSettings>.Default.GetZoomMode(),
                1, item.IsInverted, ImmutableList<VideoEffectController>.Empty);
            var image = output.Output;
            foreach (var effect in item.VideoEffects.Where(e => e.IsEnabled))
            {
                var processor = effect.CreateVideoEffect(context);
                sample.Processors.Add(processor);
                processor.SetInput(image);
                drawing = processor.Update(new EffectDescription(desc, drawing, i, source.Outputs.Count, 0, 0));
                image = processor.Output;
            }
            output.DrawingEffect.SetInput(image);
            output.DrawingEffect.Update(new EffectDescription(desc,drawing,i,source.Outputs.Count,0,0));
        }
        return sample;
    }
    internal Rect Bounds(TextItem item)
    {
        using var source = Render(item);
        var result = Rect.Empty;
        foreach (var output in source.Outputs)
        {
            var r = context.DeviceContext.GetImageLocalBounds(output.DrawingEffect.Output);
            result.Union(new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top));
        }
        return result;
    }
    private byte[] Pixels(TextItem item, out int width, out int height)
    {
        width = timeline.VideoInfo.Width + Padding * 2;
        height = timeline.VideoInfo.Height + Padding * 2;
        using var source = Render(item);
        var dc = context.DeviceContext;
        var format = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
        using var target = dc.CreateBitmap(new SizeI(width,height),IntPtr.Zero,0,new BitmapProperties1(format,96,96,BitmapOptions.Target));
        using var cpu = dc.CreateBitmap(new SizeI(width,height),IntPtr.Zero,0,new BitmapProperties1(format,96,96,BitmapOptions.CpuRead|BitmapOptions.CannotDraw));
        dc.Target = target;
        dc.Transform = Matrix3x2.CreateTranslation(width/2f,height/2f);
        dc.BeginDraw();
        dc.Clear(new Color4(0,0,0,0));
        foreach (var output in source.Outputs) dc.DrawImage(output.DrawingEffect.Output);
        dc.EndDraw();
        dc.Target = null;
        dc.Transform = Matrix3x2.Identity;
        cpu.CopyFromBitmap(target);
        var mapped = cpu.Map(MapOptions.Read);
        try
        {
            var bytes = new byte[width*height*4];
            for (var y=0;y<height;y++) Marshal.Copy(mapped.Bits + y*mapped.Pitch, bytes, y*width*4, width*4);
            return bytes;
        }
        finally { cpu.Unmap(); }
    }
    internal void CheckPixels(TextItem item, bool horizontal, bool vertical, double marginX, double marginY)
    {
        var bytes = Pixels(item,out var width,out var height);
        var left=width; var top=height; var right=-1; var bottom=-1;
        for (var y=0;y<height;y++)
        for (var x=0;x<width;x++)
            if (bytes[(y*width+x)*4+3] > 0)
            { left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x); bottom=Math.Max(bottom,y); }
        Program.Check(right>=left && bottom>=top,"render contains visible pixels");
        Program.Check(!horizontal || (left>=Padding+marginX && right<width-Padding-marginX),$"horizontal pixels within margin: {left}..{right}");
        Program.Check(!vertical || (top>=Padding+marginY && bottom<height-Padding-marginY),$"vertical pixels within margin: {top}..{bottom}");
    }
    internal void Save(TextItem item,string path)
    {
        var bytes=Pixels(item,out var width,out var height);
        Program.SavePng(BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,bytes,width*4),path);
    }
    public void Dispose() { context.Dispose(); devices.Dispose(); }

    private sealed class RenderedSample(ISource source) : IDisposable
    {
        internal IReadOnlyList<ISourceOutput> Outputs => source.Outputs;
        internal List<IVideoEffectProcessor> Processors { get; } = new();
        public void Dispose()
        {
            foreach (var output in source.Outputs) output.DrawingEffect.ClearInput();
            for (var i = Processors.Count - 1; i >= 0; i--)
            { Processors[i].ClearInput(); Processors[i].Dispose(); }
            source.Dispose();
        }
    }
}
