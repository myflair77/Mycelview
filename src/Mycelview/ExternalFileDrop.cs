using System.IO;
using System.Windows;
namespace Mycelview;
internal static class ExternalFileDrop
{
    internal static string[] Paths(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
        ? paths.Where(p=>File.Exists(p)||Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    internal static DragDropEffects Effect(string[] paths,string destination,DragDropKeyStates keys,DragDropEffects allowed,bool busy)
    {
        if(busy || paths.Length==0 || !Directory.Exists(destination) || (keys&DragDropKeyStates.AltKey)!=0)return DragDropEffects.None;
        try
        {
            if(paths.Any(p=>Directory.Exists(p)&&(FileOperationRules.Same(p,destination)||FileOperationRules.Inside(destination,p))))return DragDropEffects.None;
            bool copy=(keys&DragDropKeyStates.ControlKey)!=0 || ((keys&DragDropKeyStates.ShiftKey)==0 && paths.Any(p=>!string.Equals(Path.GetPathRoot(p),Path.GetPathRoot(destination),StringComparison.OrdinalIgnoreCase)));
            var effect=copy?DragDropEffects.Copy:DragDropEffects.Move;
            if((allowed&effect)==0)return DragDropEffects.None;
            if(!copy && paths.All(p=>FileOperationRules.Same(Path.GetDirectoryName(p)!,destination)))return DragDropEffects.None;
            return effect;
        }
        catch(ArgumentException){return DragDropEffects.None;}
    }
}
