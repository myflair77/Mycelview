using System.IO;
namespace Mycelview;

// Only inspect shortcut locations, never scan the drives or start the discovered programs.
internal static class LauncherCatalog
{
    internal static List<LauncherFavorite> Discover(IEnumerable<string>? roots=null)
    {
        bool includeInstalled=roots==null;
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
        if(includeInstalled)items.AddRange(InstalledApps());
        return items.DistinctBy(x=>x.Target,StringComparer.OrdinalIgnoreCase)
            .OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    internal static IEnumerable<LauncherFavorite> Filter(IEnumerable<LauncherFavorite> items,string query) =>
        items.Where(x=>SearchText(x).Contains(query.Trim(),StringComparison.CurrentCultureIgnoreCase));

    internal static string SearchText(LauncherFavorite item)
    {
        var text=item.Name+" "+item.Target;
        if(text.Contains("ChatGPT",StringComparison.OrdinalIgnoreCase))text+=" 챗지피티 챗GPT";
        if(text.Contains("Codex",StringComparison.OrdinalIgnoreCase))text+=" 코덱스";
        return text;
    }
    internal static List<LauncherFavorite> InstalledApps()
    {
        var result=new List<LauncherFavorite>();
        object? shell=null,folder=null,entries=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder=((dynamic)shell!).NameSpace(@"shell:AppsFolder");
            if(folder==null)return result;
            entries=((dynamic)folder).Items();
            for(int i=0;i<(int)((dynamic)entries).Count;i++){
                object? entry=null;
                try{
                    entry=((dynamic)entries).Item(i);
                    string id=Convert.ToString(((dynamic)entry).ExtendedProperty("System.AppUserModel.ID"))??"";
                    if(!id.Contains('!'))continue;
                    string name=Convert.ToString(((dynamic)entry).Name)??id;
                    result.Add(new(){Name=name,Target=@"shell:AppsFolder\"+id});
                }finally{Release(entry);}
            }
        }catch(System.Runtime.InteropServices.COMException){}
        finally{Release(entries);Release(folder);Release(shell);}
        return result;
    }
    static void Release(object? value){if(value!=null&&System.Runtime.InteropServices.Marshal.IsComObject(value))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);}
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

