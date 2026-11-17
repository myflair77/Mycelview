using System.IO;
using System.Xml.Linq;
namespace Mycelview;
internal static class PackagedApps
{
    internal const string Prefix=@"shell:AppsFolder\";
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> cache=new(StringComparer.OrdinalIgnoreCase);
    internal static string Executable(string target)
    {
        if(!target.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase))return target;
        if(cache.TryGetValue(target,out var saved)&&File.Exists(saved))return saved;
        var resolved=Resolve(target);if(resolved.Length>0)cache[target]=resolved;return resolved;
    }
    static string Resolve(string target)
    {
        if(!target.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase))return target;
        var parts=target[Prefix.Length..].Split('!',2);
        if(parts.Length!=2)return "";
        try{
            var manager=new Windows.Management.Deployment.PackageManager();
            var package=manager.FindPackagesForUser("").FirstOrDefault(p=>p.Id.FamilyName.Equals(parts[0],StringComparison.OrdinalIgnoreCase));
            if(package==null)return "";
            var root=package.InstalledLocation.Path;
            var manifest=XDocument.Load(Path.Combine(root,"AppxManifest.xml"));
            var app=manifest.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Application"&&(string?)e.Attribute("Id")==parts[1]);
            var executable=(string?)app?.Attribute("Executable");
            if(string.IsNullOrWhiteSpace(executable))return "";
            var path=Path.GetFullPath(Path.Combine(root,executable));
            return path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(path)?path:"";
        }catch(Exception ex) when(ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException){return "";}
    }
}