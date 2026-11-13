namespace Mycelview;
public sealed class CardEnlargementSettings
{
    public string Mode { get; set; } = "A";
    public bool FixedSize { get; set; } = true;
    public int Percent { get; set; } = 150;
    public int Width { get; set; } = 700;
    public int Height { get; set; } = 600;
    public int DelayMs { get; set; } = 350;
    public CardEnlargementSettings Normalize() => new() { Mode = Mode is "A" or "B" or "C" ? Mode : "A", FixedSize = FixedSize, Percent = Math.Clamp(Percent,110,300), Width = Math.Clamp(Width,400,2400), Height = Math.Clamp(Height,260,1800), DelayMs = Math.Clamp(DelayMs,0,1500) };
}
public sealed partial class MainViewModel
{
    private CardEnlargementSettings enlargement = new();
    public CardEnlargementSettings Enlargement { get => enlargement; private set { enlargement = value.Normalize(); Notify(); Notify(nameof(EnlargementLabel)); } }
    public string EnlargementLabel => "카드 확대 · " + (Enlargement.Mode == "B" ? "밀어내는 확대" : Enlargement.Mode == "C" ? "확대 안함" : "떠오르는 확대");
    public void SetEnlargement(CardEnlargementSettings settings) { Enlargement = settings; Save(); }
}
