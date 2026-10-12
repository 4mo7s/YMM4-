using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;
using YukkuriMovieMaker.Plugin.Effects;

public enum TestPositionMode { Coupled, Fixed }

// A third-party-shaped effect proves that support is based on the public
// processor contract, not on a list of YMM4 built-in class names.
public sealed class TestPositionEffect : VideoEffectBase
{
    public TestPositionMode Mode { get; set; }
    public override string Label => "Test position " + Mode;
    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new Processor(devices, Mode);
    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription description) => Array.Empty<string>();
    protected override IEnumerable<IAnimatable> GetAnimatables() => Array.Empty<IAnimatable>();

    private sealed class Processor(IGraphicsDevicesAndContext devices, TestPositionMode mode) : VideoEffectProcessorBase(devices)
    {
        private AffineTransform2D? transform;
        protected override ID2D1Image CreateEffect(IGraphicsDevicesAndContext context)
        {
            transform = new AffineTransform2D(context.DeviceContext);
            disposer.Collect(transform);
            var output = transform.Output;
            disposer.Collect(output);
            return output;
        }
        protected override void setInput(ID2D1Image? input) => transform!.SetInput(0, input, true);
        protected override void ClearEffectChain() => transform?.SetInput(0, null, true);
        public override DrawDescription Update(EffectDescription description)
        {
            var draw = description.DrawDescription.Draw;
            return description.DrawDescription with { Draw = mode == TestPositionMode.Fixed ? Vector3.Zero
                : new Vector3(2 * draw.X + 0.4f * draw.Y + 30, -0.3f * draw.X + 1.5f * draw.Y - 20, draw.Z) };
        }
    }
}
