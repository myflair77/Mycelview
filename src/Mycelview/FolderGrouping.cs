using System.Collections;
using System.Globalization;
using System.Windows.Data;
namespace Mycelview;
internal static class FolderGrouping
{
    internal static readonly (string Id,string Label)[] Modes=[("","없음"),("Type","종류별"),("Modified","날짜별 (수정한 날짜)"),("Length","크기별"),("Extension","확장명별")];
    internal static (int Order,string Label) Key(FileEntry e,string mode)
    {
        if(mode=="Type")return (e.IsDirectory?0:1,e.Type);
        if(mode=="Extension")return(e.IsDirectory?0:1,e.IsDirectory?"폴더":string.IsNullOrEmpty(e.Extension)?"확장명 없음":e.Extension);
        if(mode=="Length")
        {
            if(e.IsDirectory)return(0,"폴더");
            return e.Length switch {0=>(1,"빈 파일"),<1024*1024=>(2,"1 MB 미만"),<10*1024*1024=>(3,"1 MB 이상 ~ 10 MB 미만"),<100*1024*1024=>(4,"10 MB 이상 ~ 100 MB 미만"),<1024L*1024*1024=>(5,"100 MB 이상 ~ 1 GB 미만"),_=>(6,"1 GB 이상")};
        }
        if(e.Modified==DateTime.MinValue)return(6,"날짜 없음");
        var days=(DateTime.Today-e.Modified.Date).Days;
        return days switch {<0=>(0,"미래 날짜"),0=>(1,"오늘"),1=>(2,"어제"),<7=>(3,"최근 7일"),<30=>(4,"최근 30일"),_=>(5,"이전")};
    }
    internal sealed class Comparer(string mode,string property,bool descending):IComparer
    {
        readonly FileEntryComparer files=new(property,descending);
        public int Compare(object? x,object? y)
        {
            if(x is not FileEntry a||y is not FileEntry b)return 0;
            var left=Key(a,mode);var right=Key(b,mode);
            int order=left.Order.CompareTo(right.Order);
            if(order==0)order=StringComparer.CurrentCultureIgnoreCase.Compare(left.Label,right.Label);
            return order!=0?order:files.Compare(x,y);
        }
    }
    internal sealed class LabelConverter(string mode):IValueConverter
    {
        public object Convert(object value,Type target,object parameter,CultureInfo culture)=>value is FileEntry entry?Key(entry,mode).Label:"";
        public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
