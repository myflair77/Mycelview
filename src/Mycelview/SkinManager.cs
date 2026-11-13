using System.Windows;
namespace Mycelview;
public sealed record SkinOption(string Id, string Name);
public static class SkinManager
{
 public const string DefaultId = "windows11";
 public static IReadOnlyList<SkinOption> Options { get; } = [new(DefaultId, "Windows 11 (기본)"), new("vercel", "Vercel · 모노크롬")];
 private static ResourceDictionary? active;
 public static string Normalize(string? id) => Options.Any(s => s.Id == id) ? id! : DefaultId;
 public static string Apply(string? id)
 {
  id = Normalize(id);
  var next = new ResourceDictionary { Source = new Uri($"Themes/{id}.xaml", UriKind.Relative) };
  var dictionaries = Application.Current.Resources.MergedDictionaries;
  dictionaries.Add(next);
  if (active != null) dictionaries.Remove(active);
  active = next;
  return id;
 }
}
