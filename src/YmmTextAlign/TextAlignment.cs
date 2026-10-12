using YukkuriMovieMaker.Project.Items;

namespace YmmTextAlign;

internal static class TextAlignment
{
    internal static BasePoint WithHorizontal(BasePoint point, int column) => point switch
    {
        BasePoint.LeftTop or BasePoint.CenterTop or BasePoint.RightTop =>
            new[] { BasePoint.LeftTop, BasePoint.CenterTop, BasePoint.RightTop }[column],
        BasePoint.LeftCenter or BasePoint.CenterCenter or BasePoint.RightCenter =>
            new[] { BasePoint.LeftCenter, BasePoint.CenterCenter, BasePoint.RightCenter }[column],
        BasePoint.LeftBottom or BasePoint.CenterBottom or BasePoint.RightBottom =>
            new[] { BasePoint.LeftBottom, BasePoint.CenterBottom, BasePoint.RightBottom }[column],
        // Horizontal text alignment does not change vertical-writing settings.
        _ => point
    };
}
