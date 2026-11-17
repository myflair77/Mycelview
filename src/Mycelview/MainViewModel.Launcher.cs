namespace Mycelview;
public sealed record LauncherGroup(string Kind,string Name,List<LauncherFavorite> Items);
public sealed partial class MainViewModel
{
    private List<string> launcherGroupOrder=["App","Browser","Website"];
    public string LauncherGroupsTitle => string.Join("·", LauncherGroups.Select(g => g.Name));
    public List<LauncherGroup> LauncherGroups => launcherGroupOrder.Select(k=>new LauncherGroup(k,k=="App"?"앱":k=="Browser"?"브라우저":"사이트",LauncherFavorites.Where(x=>x.Kind==k).ToList())).ToList();
    internal void LoadLauncherGroupOrder(IEnumerable<string>? order)
    {
        launcherGroupOrder=(order??[]).Concat(new[]{"App","Browser","Website"}).Where(x=>x is "App" or "Browser" or "Website").Distinct().ToList();
        Notify(nameof(LauncherGroups)); Notify(nameof(LauncherGroupsTitle));
    }
    internal bool InsertLauncherGroup(string source,string target,bool after)
    {
        if(source==target||!launcherGroupOrder.Contains(source)||!launcherGroupOrder.Contains(target))return false;
        launcherGroupOrder.Remove(source);
        launcherGroupOrder.Insert(launcherGroupOrder.IndexOf(target)+(after?1:0),source);
        Notify(nameof(LauncherGroups)); Notify(nameof(LauncherGroupsTitle));return true;
    }
    internal bool InsertLauncherItem(Guid source,Guid target,bool after)
    {
        var item=LauncherFavorites.FirstOrDefault(x=>x.Id==source);
        var other=LauncherFavorites.FirstOrDefault(x=>x.Id==target);
        if(item==null||other==null||item==other||item.Kind!=other.Kind)return false;
        int from=LauncherFavorites.IndexOf(item),to=LauncherFavorites.IndexOf(other)+(after?1:0);
        if(from<to)to--;
        if(from==to)return false;
        LauncherFavorites.Move(from,to);return true;
    }
    internal bool MoveLauncherGroup(string kind,int direction)
    {
        int index=launcherGroupOrder.IndexOf(kind),next=index+direction;
        if(index<0||next<0||next>=launcherGroupOrder.Count)return false;
        (launcherGroupOrder[index],launcherGroupOrder[next])=(launcherGroupOrder[next],launcherGroupOrder[index]);
        Notify(nameof(LauncherGroups)); Notify(nameof(LauncherGroupsTitle));return true;
    }
    internal bool MoveLauncherItem(LauncherFavorite item,int direction)
    {
        var siblings=LauncherFavorites.Where(x=>x.Kind==item.Kind).ToList();
        int index=siblings.IndexOf(item),next=index+direction;
        if(index<0||next<0||next>=siblings.Count)return false;
        LauncherFavorites.Move(LauncherFavorites.IndexOf(item),LauncherFavorites.IndexOf(siblings[next]));
        return true;
    }
}


