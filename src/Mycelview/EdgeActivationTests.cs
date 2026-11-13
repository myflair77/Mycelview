using System.Text.Json;
namespace Mycelview;
internal static class EdgeActivationTests
{
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var left = new EdgeMonitor("left", -1920, -200, 0, 880, false);
        var primary = new EdgeMonitor("primary", 0, 0, 2560, 1440, true);
        Check(EdgeActivationService.SelectMonitor([primary, left], "Leftmost") == left, "Default must select leftmost, not primary monitor.");
        Check(EdgeActivationService.SelectMonitor([primary, left], "primary") == primary, "Explicit monitor must override default.");
        Check(EdgeActivationService.SelectMonitor([primary], "left") == null, "Disconnected selected monitor must not trigger on another display.");
        Check(EdgeActivationService.OnEdge(left, "Left", -1920, -200) && EdgeActivationService.OnEdge(left, "Left", -1920, 879), "Entire left edge must trigger, including negative coordinates.");
        Check(!EdgeActivationService.OnEdge(left, "Left", -1917, 0) && !EdgeActivationService.OnEdge(left, "Left", 0, 0), "Interior and other monitors must not trigger.");
        Check(EdgeActivationService.OnEdge(primary, "Right", 2559, 700) && EdgeActivationService.OnEdge(primary, "Top", 100, 0) && EdgeActivationService.OnEdge(primary, "Bottom", 100, 1439), "Other configured edges must work.");
        var partial = new EdgeActivationSettings { Edge = "Left", StartPercent = 25, EndPercent = 75 };
        Check(EdgeActivationService.InRange(left, partial, -1920, 300) && !EdgeActivationService.InRange(left, partial, -1920, -200) && !EdgeActivationService.InRange(left, partial, -1920, 879), "Partial vertical range must only activate the selected section.");
        var marker = EdgeActivationMarker.Bounds(left, partial);
        Check(marker.X == -1920 && marker.Y == 70 && marker.Width == 10 && marker.Height == 540, "Blue marker must match the chosen vertical range on negative-coordinate monitors.");
        Check(new EdgeActivationSettings().ThicknessPixels == 10, "Marker default must be 10px.");
        foreach (int scale in new[] { 2, 10, 30 })
        {
            partial.ThicknessPixels = scale;
            Check(EdgeActivationMarker.Bounds(left, partial).Width == scale, "Marker thickness must match the selected pixel value.");
        }
        var thicknessSaved = JsonSerializer.Deserialize<EdgeActivationSettings>(JsonSerializer.Serialize(partial))!;
        Check(thicknessSaved.ThicknessPixels == 30, "Marker thickness must persist.");
        var legacyThickness = JsonSerializer.Deserialize<EdgeActivationSettings>("{\"ThicknessScale\":3}")!.Normalize();
        Check(legacyThickness.ThicknessPixels == 12 && legacyThickness.ThicknessScale == null, "Legacy multiplier must migrate to equivalent pixels.");
        Check(new EdgeActivationSettings { ThicknessPixels = 100 }.Normalize().ThicknessPixels == 30 && new EdgeActivationSettings { ThicknessPixels = 0 }.Normalize().ThicknessPixels == 2, "Pixel thickness must be bounded.");
        partial.Edge = "Top";
        Check(EdgeActivationService.InRange(primary, partial, 1280, 0) && !EdgeActivationService.InRange(primary, partial, 100, 0), "Horizontal range must run left to right.");
        var rangeRoundTrip = JsonSerializer.Deserialize<EdgeActivationSettings>(JsonSerializer.Serialize(partial))!;
        Check(rangeRoundTrip.StartPercent == 25 && rangeRoundTrip.EndPercent == 75, "Custom range must persist.");
        Check(EdgeActivationService.SelectMonitor([primary, left], "Automatic", "Right") == primary, "Right edge must automatically select rightmost monitor.");
        Check(EdgeActivationService.SelectMonitor([primary, left], "Automatic", "Top") == left, "Top edge must select topmost monitor.");
        Check(EdgeActivationService.SelectMonitor([primary, left], "Automatic", "Bottom") == primary, "Bottom edge must select bottommost monitor.");
        var dwell = new EdgeDwell();
        Check(!dwell.Update("left", 0, 500) && !dwell.Update("left", 499, 500) && dwell.Update("left", 500, 500), "Trigger must wait 500ms.");
        Check(!dwell.Update("left", 2000, 500), "Remaining at edge must not repeatedly steal focus.");
        dwell.Update(null, 2001, 500); Check(!dwell.Update("left", 2010, 500) && dwell.Update("left", 2510, 500), "Leaving must rearm trigger.");
        dwell.Reset(); Check(!dwell.Update("left", 3000, 500) && !dwell.Update("right", 3400, 500) && !dwell.Update("right", 3800, 500), "Changing zone must restart dwell.");
        var defaults = new EdgeActivationSettings(); Check(defaults.StartPercent == 50 && defaults.EndPercent == 100 && defaults.Enabled && defaults.Edge == "Left" && defaults.Monitor == "Automatic" && defaults.DelayMs == 100, "Requested defaults must apply.");
        var saved = StateStore.Parse(JsonSerializer.Serialize(new WorkspaceState { EdgeActivation = new() { Enabled = false, Edge = "Bottom", Monitor = "left", DelayMs = 1200 } }));
        Check(!saved.EdgeActivation.Enabled && saved.EdgeActivation.Edge == "Bottom" && saved.EdgeActivation.Monitor == "left" && saved.EdgeActivation.DelayMs == 1200, "All settings must survive serialization.");
        Check(EdgeActivationService.GetMonitors().Count > 0, "Native monitor enumeration must find displays.");
    }
}
