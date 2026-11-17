using System.IO;
namespace Mycelview;

// Only inspect shortcut locations, never scan the drives or start the discovered programs.
internal static class LauncherCatalog
{
    internal static List<LauncherFavorite> Discover(IEnumerable<string>? roots=null)
    {
        roots ??= new[]{
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)};
        var items=new List<LauncherFavorite>();
        var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
        foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!Directory.Exists(root))continue;
            try
            {
                foreach(var path in Directory.EnumerateFiles(root,"*",options))
                    if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase) ||
                       Path.GetExtension(path).Equals(".appref-ms",StringComparison.OrdinalIgnoreCase))
                        items.Add(new(){Name=Path.GetFileNameWithoutExtension(path),Target=path});
            }
            catch(IOException){} catch(UnauthorizedAccessException){}
        }
        return items.DistinctBy(x=>x.Target,StringComparer.OrdinalIgnoreCase)
            .OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    internal static IEnumerable<LauncherFavorite> Filter(IEnumerable<LauncherFavorite> items,string query) =>
        items.Where(x=>x.Name.Contains(query.Trim(),StringComparison.CurrentCultureIgnoreCase));

    internal static List<LauncherFavorite> FromDrop(IEnumerable<string> paths)
    {
        var items=new List<LauncherFavorite>();
        foreach(var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item=new LauncherFavorite{Name=Path.GetFileNameWithoutExtension(path),Target=path};
            try{LauncherService.Prepare(item);items.Add(item);}
            catch(Exception ex) when(ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException){}
        }
        return items;
    }
}

