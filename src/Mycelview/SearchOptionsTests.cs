using System.IO;
namespace Mycelview;
internal static class SearchOptionsTests
{
    internal static async Task RunAsync(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        string root = Path.Combine(output, "options-fixture"), folder = Path.Combine(root, "예산");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "보고서.txt"), "fixture");
        File.WriteAllText(Path.Combine(root, "예산안.txt"), "fixture");
        File.WriteAllText(Path.Combine(root, "여행 사진.jpg"), "fixture");
        File.WriteAllText(Path.Combine(root, "여행 사진.png"), "fixture");
        File.WriteAllText(Path.Combine(root, "여행 사진 모음.txt"), "fixture");
        Directory.CreateDirectory(Path.Combine(root, "version.1"));
        using var index = new LocalSearchIndex(Path.Combine(output, "options-index"));
        try
        {
            index.Start([root]); await index.InitialPass;
            async Task<IndexResults> Find(SearchOptions options, string query = "예산", int limit = 20) => await index.QueryAsync(query, limit, default, options);
            Check((await Find(new())).Items.Count == 2, "Default search must compare only each item name.");
            Check((await Find(new() { IncludeParentFolders = true })).Items.Count == 3, "Enabled parent matching must include descendants.");
            Check((await Find(new() { IncludeParentFolders = false })).Items.Count == 2, "Names-only must exclude descendants whose names do not match.");
            Check((await Find(new() { Target = "Files" })).Items.Count == 1, "File-only filter must exclude folders.");
            var folders = await Find(new() { Target = "Folders" }, limit: 1);
            Check(folders.Items.Count == 1 && !folders.More && folders.Items[0].IsDirectory, "Type filtering must happen before pagination.");
            Check((await Find(new() { Exact = true, IncludeParentFolders = true })).Items.Count == 2, "Exact parent folder match must include its descendants, not similar names.");
            Check((await Find(new() { Exact = true, IncludeParentFolders = false })).Items.Count == 1, "Exact name-only match must exclude descendants.");
            Check((await Find(new() { Exact = true }, "보고서")).Items.Count == 1, "Exact file matching must ignore extension.");
            Check((await Find(new() { Exact = true, IncludeParentFolders = false }, "여행 사진")).Items.Count == 2, "Exact file names must preserve spaces and match across extensions, excluding longer names.");
            Check((await Find(new() { Exact = true }, "version.1")).Items.Count == 1 && (await Find(new() { Exact = true }, "version")).Items.Count == 0, "Dots in folder names must not be treated as file extensions.");
            Check((await Find(new(), "ㅇㅅ")).Items.Count == 2 && (await Find(new() { UseInitials = false }, "ㅇㅅ")).Items.Count == 0, "Initial consonant toggle must affect matching.");
            Check((await Find(new(), "예산 !보고서")).Items.Count == 2, "Boolean operators must still apply.");
        }
        finally { await index.StopAsync(); }
    }
}
