using System.Windows.Media;
namespace Mycelview;
// Shared by the settings diagram and passive desktop markers.
internal static class EdgeRegionPalette
{
    private static EdgeActivationSettings? cachedSettings;
    private static List<EdgeMonitor> cachedMonitors=[];
    private static Dictionary<string,Color> colors=[];
    internal static Color Get(EdgeActivationSettings settings,List<EdgeMonitor> monitors,string key)
    {
        if(!ReferenceEquals(cachedSettings,settings)||!cachedMonitors.SequenceEqual(monitors)){
            var zones=ScreenEdgePicker.Zones(settings,settings.AppZones??[]);
            var brushes=ScreenEdgePicker.RegionColors(zones,monitors);
            colors=zones.Select((zone,i)=>(zone.Key,Color:((SolidColorBrush)brushes[i]).Color)).ToDictionary(x=>x.Key,x=>x.Color);
            cachedSettings=settings;cachedMonitors=monitors.ToList();
        }
        return colors.TryGetValue(key,out var color)?color:Colors.DodgerBlue;
    }
}
