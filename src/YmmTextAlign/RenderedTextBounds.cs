using System.Collections.Immutable;
using System.Numerics;
using System.Windows;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using FrameTime = YukkuriMovieMaker.Player.Video.FrameTime;

namespace YmmTextAlign;

// Public rendering interfaces; no effect type names or private members.
internal sealed class RenderedTextBounds : IDisposable
{
    private readonly GraphicsDevices devices;
    private readonly IGraphicsDevicesAndContext context;

    internal RenderedTextBounds()
    {
        devices = new GraphicsDevices();
        try { context = devices.CreateContext(); }
        catch { devices.Dispose(); throw; }
    }

    internal Session CreateSession(TextItem original, BasePoint point, int frame, Timeline timeline, Scenes scenes) =>
        new(devices, context, original, point, frame, timeline, scenes);

    public void Dispose() { context.Dispose(); devices.Dispose(); }

    internal sealed class Session : IDisposable
    {
        private readonly IGraphicsDevicesAndContext context;
        private readonly TextItem item;
        private readonly TimelineItemSourceDescription description;
        private readonly int frame;
        private readonly int fps;
        private ISource? source;
        private readonly List<OutputChain> chains = new();

        internal Session(IGraphicsDevices devices, IGraphicsDevicesAndContext context, TextItem original,
            BasePoint point, int frame, Timeline timeline, Scenes scenes)
        {
            if (string.IsNullOrWhiteSpace(original.Text))
                throw new InvalidOperationException("空のテキストは整列できません");
            if (original.IsClippingWithObjectAbove)
                throw new NotSupportedException("上のオブジェクトでクリッピングする設定は未対応です");
            if (original.IsDevidedPerCharacter || original.DisplayInterval != 0 || original.HideInterval != 0)
                throw new NotSupportedException("一文字ずつ表示・個別文字描画は未対応です");

            this.context = context;
            this.frame = frame;
            fps = timeline.VideoInfo.FPS;
            // A single clone and processor chain keep random/stateful effects
            // consistent while measuring candidate positions at the same frame.
            item = (TextItem)original.GetClone();
            item.BasePoint = point;
            var scene = new Scene(timeline, scenes, Array.Empty<Guid>());
            var timelineDescription = new TimelineSourceDescription(
                new System.Drawing.Size(timeline.VideoInfo.Width, timeline.VideoInfo.Height),
                new FrameTime(item.Frame + frame, fps), new FrameTime(item.Frame + item.Length, fps),
                fps, TimelineSourceUsage.Paused, scene.ID, scene.AllScenesWithParentScenesInfo);
            description = new TimelineItemSourceDescription(timelineDescription, frame, item.Length, item.Layer);
            try
            {
                source = item.CreateVideoSource(devices, scene);
                source.Update(description);
                foreach (var output in source.Outputs)
                {
                    if (output.TimeOffset != TimeSpan.Zero || output.DurationOffset != TimeSpan.Zero)
                        throw new NotSupportedException("文字ごとに時間が変わる描画は未対応です");
                    var chain = new OutputChain(output);
                    chains.Add(chain);
                    // Keep effect identity for effects whose random seed uses the
                    // model object. Separate processors read settings through the
                    // public contract; no effect settings are edited here.
                    foreach (var effect in original.VideoEffects.Where(e => e.IsEnabled))
                        chain.Processors.Add(effect.CreateVideoEffect(context));
                }
            }
            catch { Dispose(); throw; }
        }

        internal Rect Measure(double x, double y)
        {
            item.X.AddToEachValues(x - Value(item.X));
            item.Y.AddToEachValues(y - Value(item.Y));
            var zoom = Value(item.Zoom) / 100;
            var rotation = Value(item.Rotation);
            var z = Value(item.Z);
            if (!double.IsFinite(zoom) || zoom == 0 || !double.IsFinite(rotation) || !double.IsFinite(z))
                throw new InvalidOperationException("拡大率・回転・Z座標が無効です");
            var bounds = Rect.Empty;
            for (var i = 0; i < chains.Count; i++)
            {
                var chain = chains[i];
                var output = chain.Output;
                var drawing = new DrawDescription(new Vector3((float)Value(item.X), (float)Value(item.Y), (float)z)
                    + new Vector3(output.DrawingOffset, 0), output.Center, new Vector2((float)zoom),
                    new Vector3(0, 0, (float)rotation), Matrix4x4.Identity,
                    (Vortice.Direct2D1.InterpolationMode)SettingsBase<YMMSettings>.Default.GetZoomMode(),
                    1, item.IsInverted, ImmutableList<VideoEffectController>.Empty);
                var image = output.Output;
                foreach (var processor in chain.Processors)
                {
                    processor.SetInput(image);
                    drawing = processor.Update(new EffectDescription(description, drawing, i, chains.Count, 0, 0));
                    image = processor.Output;
                }
                output.DrawingEffect.SetInput(image);
                output.DrawingEffect.Update(new EffectDescription(description, drawing, i, chains.Count, 0, 0));
                var rendered = context.DeviceContext.GetImageLocalBounds(output.DrawingEffect.Output);
                if (!float.IsFinite(rendered.Left) || !float.IsFinite(rendered.Top) ||
                    !float.IsFinite(rendered.Right) || !float.IsFinite(rendered.Bottom))
                    throw new InvalidOperationException("エフェクト適用後の描画範囲が有限ではありません");
                if (rendered.Right > rendered.Left && rendered.Bottom > rendered.Top)
                    bounds.Union(new Rect(rendered.Left, rendered.Top,
                        (double)rendered.Right - rendered.Left, (double)rendered.Bottom - rendered.Top));
            }
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("このフレームに測定できる文字がありません");
            bounds.Inflate(1, 1);
            return bounds;
        }

        private double Value(Animation animation) => animation.GetValue(frame, item.Length, fps);
        public void Dispose()
        {
            foreach (var chain in chains)
            {
                chain.Output.DrawingEffect.ClearInput();
                for (var i = chain.Processors.Count - 1; i >= 0; i--)
                { chain.Processors[i].ClearInput(); chain.Processors[i].Dispose(); }
            }
            chains.Clear();
            source?.Dispose();
            source = null;
        }
        private sealed class OutputChain(ISourceOutput output)
        {
            internal ISourceOutput Output { get; } = output;
            internal List<IVideoEffectProcessor> Processors { get; } = new();
        }
    }
}
