using System.IO;
using System.Runtime.InteropServices;
namespace Mycelview;
internal static class LauncherEdgeSetup
{
    internal static string Executable(LauncherFavorite favorite)
    {
        var path=favorite.Target;
        if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase)){
            object? shell=null,link=null;
            try{
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                link=((dynamic)shell!).CreateShortcut(path);
                path=Environment.ExpandEnvironmentVariables((string)((dynamic)link).TargetPath);
            }finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        }
        if(!File.Exists(path)||!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("이 바로가기에서 실행 파일을 확인할 수 없습니다. 실행 파일(.exe)로 등록한 앱을 선택하세요.");
        return Path.GetFullPath(path);
    }
    internal static AppWindowZone Prepare(EdgeActivationSettings settings,LauncherFavorite favorite)
    {
        var path=Executable(favorite);
        var zone=settings.AppZones.FirstOrDefault(z=>StringComparer.OrdinalIgnoreCase.Equals(z.AppPath,path));
        if(zone==null){
            zone=new(){AppPath=path,Name=favorite.Name};
            bool assigned=false;
            foreach(var edge in new[]{"Left","Right","Top","Bottom"}){
                for(int start=0;start<100;start+=25){
                    if(settings.AppZones.Any(z=>z.Edge==edge&&z.StartPercent<start+25&&z.EndPercent>start))continue;
                    zone.Edge=edge;zone.StartPercent=start;zone.EndPercent=start+25;assigned=true;break;
                }
                if(assigned)break;
            }
            settings.AppZones.Add(zone);
        }else{
            // Bring the chosen app's existing section into view at the bottom of the editor.
            settings.AppZones.Remove(zone);settings.AppZones.Add(zone);
        }
        settings.Enabled=true;
        return zone;
    }
}
