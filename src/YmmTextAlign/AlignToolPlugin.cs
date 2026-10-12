using YukkuriMovieMaker.Plugin;

namespace YmmTextAlign;

public sealed class AlignToolPlugin : IToolPlugin
{
    public string Name => "テキスト整列";
    public Type ViewModelType => typeof(AlignToolViewModel);
    public Type ViewType => typeof(AlignToolView);
    public bool AllowMultipleInstances => false;
}
