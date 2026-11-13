namespace Mycelview;
public sealed partial class MainViewModel
{
    private int textScalePercent = 100;
    public int TextScalePercent
    {
        get => textScalePercent;
        set
        {
            var next = Math.Clamp(value, 70, 200);
            if (textScalePercent == next) { TextScale.Apply(next); return; }
            textScalePercent = next; TextScale.Apply(next);
            Notify(); Notify(nameof(TextScaleLabel)); Notify(nameof(CanReduceText)); Notify(nameof(CanEnlargeText));
            foreach (var card in Folders) card.RefreshDisplayHeight();
            Changed();
        }
    }
    public string TextScaleLabel => $"{TextScalePercent}%";
    public bool CanReduceText => TextScalePercent > 70;
    public bool CanEnlargeText => TextScalePercent < 200;
}
