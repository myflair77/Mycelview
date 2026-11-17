using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Microsoft.Win32;

namespace Mycelview;

public sealed class LauncherFavorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "App";
    public string Target { get; set; } = "";
    public string BrowserPath { get; set; } = "";
    [JsonIgnore] public string Description => Kind == "Website" ? Target + "\n브라우저: " + (BrowserPath.Length == 0 ? "시스템 기본" : BrowserPath) : Target;
    [JsonIgnore] public ImageSource Icon => ShellIcons.ForPath(Kind == "Website" ? "website.html" : Target, false);
    public LauncherFavorite Copy() => new() { Id=Id, Name=Name, Kind=Kind, Target=Target, BrowserPath=BrowserPath };
}

internal static class LauncherService
{
    internal static ProcessStartInfo Prepare(LauncherFavorite item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) throw new InvalidDataException("이름을 입력하세요.");
        if (item.Kind == "Website")
        {
            if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https"))
                throw new InvalidDataException("http:// 또는 https://로 시작하는 웹사이트 주소를 입력하세요.");
            if (string.IsNullOrWhiteSpace(item.BrowserPath)) return new(url.AbsoluteUri) { UseShellExecute=true };
            if (!File.Exists(item.BrowserPath) || !Path.GetExtension(item.BrowserPath).Equals(".exe",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("브라우저 실행 파일(.exe)을 다시 선택하세요.");
            var start = new ProcessStartInfo(item.BrowserPath) { UseShellExecute=true };
            start.ArgumentList.Add(url.AbsoluteUri);
            return start;
        }
        if (item.Kind != "App" && item.Kind != "Browser") throw new InvalidDataException("등록 종류를 선택하세요.");
        if(item.Target.StartsWith(@"shell:AppsFolder\",StringComparison.OrdinalIgnoreCase)){
            var id=item.Target[@"shell:AppsFolder\".Length..];
            if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[A-Za-z0-9_.-]+![A-Za-z0-9_.-]+$"))
                throw new InvalidDataException("설치 앱 식별자가 올바르지 않습니다.");
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")){UseShellExecute=true};
            start.ArgumentList.Add(item.Target);
            return start;
        }
        if (!File.Exists(item.Target) || !(new[]{".exe",".lnk",".appref-ms"}).Contains(Path.GetExtension(item.Target),StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("프로그램 실행 파일 또는 바로가기를 선택하세요.");
        return new(item.Target) { UseShellExecute=true, WorkingDirectory=Path.GetDirectoryName(item.Target) ?? "" };
    }

    internal static List<BrowserChoice> Browsers()
    {
        var found = new List<BrowserChoice> { new("시스템 기본 브라우저","") };
        foreach(var spec in new[]{("Chrome","chrome.exe",@"Google\Chrome\Application\chrome.exe"),("Edge","msedge.exe",@"Microsoft\Edge\Application\msedge.exe"),("Whale","whale.exe",@"Naver\Naver Whale\Application\whale.exe"),("Firefox","firefox.exe",@"Mozilla Firefox\firefox.exe")})
        {
            var paths = new List<string>();
            foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})
            {
                using var key=hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+spec.Item2);
                if(key?.GetValue(null) is string registered) paths.Add(registered.Trim('"'));
            }
            foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)})
                paths.Add(Path.Combine(root,spec.Item3));
            var path=paths.FirstOrDefault(File.Exists);
            if(path!=null) found.Add(new(spec.Item1,path));
        }
        return found;
    }
}
internal sealed record BrowserChoice(string Name,string Path);
