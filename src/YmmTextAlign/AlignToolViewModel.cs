using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project.Items;

namespace YmmTextAlign;

public sealed class AlignToolViewModel : IToolViewModel, ITimelineToolViewModel, INotifyPropertyChanged
{
    private TimelineToolInfo? info;
    private bool anchorPointBasis;
    private bool changeTextAlignment;
    private double horizontalMargin = 80;
    private double verticalMargin = 80;
    private string status = "テキストを選択してください";

    public string Title => "テキスト整列 v0.3.0";
    public bool CanSuspend => true;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
    public bool AnchorPointBasis { get => anchorPointBasis; set { anchorPointBasis = value; Notify(); } }
    public bool ChangeTextAlignment { get => changeTextAlignment; set { changeTextAlignment = value; Notify(); } }
    public double HorizontalMargin { get => horizontalMargin; set { horizontalMargin = value; Notify(); } }
    public double VerticalMargin { get => verticalMargin; set { verticalMargin = value; Notify(); } }
    public string Status { get => status; private set { status = value; Notify(); } }

    public void SetTimelineToolInfo(TimelineToolInfo toolInfo)
    {
        info = toolInfo;
        Status = "テキストを選択してください";
    }
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public void ReportFailure(Exception exception) => Status = $"整列に失敗しました: {exception.GetBaseException().Message}";

    public bool TrySetMargins(string horizontal, string vertical)
    {
        if (!double.TryParse(horizontal, NumberStyles.Float, CultureInfo.CurrentCulture, out var x) ||
            !double.TryParse(vertical, NumberStyles.Float, CultureInfo.CurrentCulture, out var y) ||
            !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0)
        { Status = "余白は0以上の数値で指定してください"; return false; }
        HorizontalMargin = x;
        VerticalMargin = y;
        return true;
    }

    // A null axis is left untouched, including its animation values.
    public bool Align(int? horizontal, int? vertical)
    {
        if (info is null) { Status = "タイムラインがありません"; return false; }
        if (horizontal is null && vertical is null) return false;
        if (horizontal is < 0 or > 2 || vertical is < 0 or > 2)
        { Status = "整列方向が無効です"; return false; }

        var timeline = info.Timeline;
        if (!double.IsFinite(HorizontalMargin) || !double.IsFinite(VerticalMargin) ||
            HorizontalMargin < 0 || VerticalMargin < 0 ||
            HorizontalMargin >= timeline.VideoInfo.Width / 2.0 ||
            VerticalMargin >= timeline.VideoInfo.Height / 2.0)
        { Status = "余白は0以上、画面の半分未満にしてください"; return false; }
        var selected = timeline.SelectedItems.OfType<TextItem>().Where(x => !x.IsLocked).ToArray();
        if (selected.Length == 0) { Status = "編集可能なテキストを選択してください"; return false; }

        var plans = new List<Placement>();
        var skipped = new List<string>();
        try
        {
            using var renderer = new RenderedTextBounds();
            foreach (var item in selected)
            {
                try
                {
                    var frame = timeline.CurrentFrame >= item.Frame && timeline.CurrentFrame < item.Frame + item.Length
                        ? timeline.CurrentFrame - item.Frame : 0;
                    var fps = timeline.VideoInfo.FPS;
                    var point = ChangeTextAlignment && horizontal.HasValue
                        ? TextAlignment.WithHorizontal(item.BasePoint, horizontal.Value) : item.BasePoint;
                    var x = item.X.GetValue(frame, item.Length, fps);
                    var y = item.Y.GetValue(frame, item.Length, fps);
                    using var session = renderer.CreateSession(item, point, frame, timeline, info.Scenes);
                    var position = RenderedPlacement.Solve(session.Measure, x, y, horizontal, vertical,
                        timeline.VideoInfo.Width, timeline.VideoInfo.Height, HorizontalMargin, VerticalMargin, AnchorPointBasis);
                    var dx = horizontal.HasValue ? position.X - x : 0;
                    var dy = vertical.HasValue ? position.Y - y : 0;
                    if (!double.IsFinite(dx) || !double.IsFinite(dy))
                        throw new InvalidOperationException("座標が無効です");
                    plans.Add(new Placement(item, point, dx, dy));
                }
                catch (Exception ex) { skipped.Add(ex.GetBaseException().Message); }
            }
        }
        catch (Exception ex)
        {
            Status = $"描画の準備に失敗しました: {ex.GetBaseException().Message}";
            return false;
        }
        if (plans.Count == 0)
        {
            Status = $"変更しませんでした: {string.Join(" / ", skipped.Distinct())}";
            return false;
        }

        info.UndoRedoManager.Record();
        foreach (var plan in plans)
        {
            plan.Item.BasePoint = plan.Point;
            if (horizontal.HasValue) plan.Item.X.AddToEachValues(plan.Dx);
            if (vertical.HasValue) plan.Item.Y.AddToEachValues(plan.Dy);
        }
        info.UndoRedoManager.Record();
        Status = skipped.Count == 0 ? $"{plans.Count}件を{(AnchorPointBasis ? "アンカーポイント" : "外形")}基準で整列しました"
            : $"{plans.Count}件を整列、{skipped.Count}件は変更なし: {string.Join(" / ", skipped.Distinct())}";
        return true;
    }

    private sealed record Placement(TextItem Item, BasePoint Point, double Dx, double Dy);
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
