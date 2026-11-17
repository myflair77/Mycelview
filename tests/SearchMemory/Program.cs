using Mycelview;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static long Heap() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
static void Put(SearchMemoryCache cache, long id, string text, bool folder = false) => cache.Put(id, text.ToUpperInvariant(), SearchQuery.Initials(text).ToUpperInvariant(), folder);
static void Tests()
{
    var cache = new SearchMemoryCache();
    var paths = new[] { @"C:\문서\여행 사진.jpg", @"C:\문서\여행 사진.png", @"C:\문서\여행 사진 모음.txt", @"C:\문서\version.1", @"C:\문서\여행\보고서.txt", @"C:\문서\여행", @"C:\other\100%_file.txt", @"C:\other\.hidden", @"C:\" + new string('a', 600) + @"\여행.txt" };
    for (int i = 0; i < paths.Length; i++) Put(cache, i+1, paths[i], i is 3 or 5);
    int comparisons = 0;
    foreach (bool parents in new[] {false, true}) foreach(bool exact in new[] {false,true}) foreach(bool initials in new[] {false,true}) foreach(string target in new[]{"All","Files","Folders"})
    foreach (string query in new[]{"여행", "ㅇㅎ", "문서", "여행 사진", "version.1", "100%_", ".hidden", "여행 OR version", "여행 !모음", "(문서 & !보고서) OR other", @"여행\보고서", "no_match"})
    {
        var options = new SearchOptions { IncludeParentFolders=parents, Exact=exact, UseInitials=initials, Target=target };
        var plan = SearchQuery.Compile(query, options);
        var expected=paths.Select((path,i)=>(path,i)).Where(x=>options.Accepts(x.i is 3 or 5) && plan.Match(options.SearchText(x.path,x.i is 3 or 5))).Select(x=>(long)x.i+1).ToArray();
        Check(cache.Find(plan,100,default,options).SequenceEqual(expected), $"Mismatch: {query}, {parents}, {exact}, {initials}, {target}"); comparisons++;
    }
    cache.RemoveTree(@"C:\문서\여행".ToUpperInvariant());
    Check(!cache.Find(SearchQuery.Compile("보고서"),100,default).Contains(5), "Subtree deletion");
    Put(cache,100,@"C:\new\fresh.txt"); Put(cache,101,@"C:\new\fresh.txt");
    Check(cache.Find(SearchQuery.Compile("fresh"),100,default).SequenceEqual(new long[]{101}), "Slot reuse and update");
    var cancel=new CancellationToken(true); bool cancelled=false;
    try {cache.Find(SearchQuery.Compile("x"),100,cancel);} catch(OperationCanceledException){cancelled=true;}
    Check(cancelled,"Cancellation");
    Console.WriteLine($"PASS: {comparisons} search combinations, subtree deletion, reuse, updates, cancellation");
}
[MethodImpl(MethodImplOptions.NoInlining)] static WeakReference CreateReleased()
{
    var c = new SearchMemoryCache(); for(int i=0;i<10000;i++) Put(c,i,$@"C:\released\{i}\file.txt"); return new WeakReference(c);
}
if(args.Length==0 || args[0]=="--self-test")
{
    Tests();
    var cache=new SearchMemoryCache();
    void Cycle(int round) { for(int i=0;i<5000;i++) Put(cache,i,$@"C:\churn{round}\folder{i}\file.txt"); cache.RemoveTree($@"C:\CHURN{round}"); }
    for(int i=0;i<3;i++) Cycle(i);
    long before=Heap(); for(int i=3;i<33;i++)Cycle(i); long after=Heap();
    Check(after-before<1024*1024,$"Churn retained {after-before} bytes");
    var weak=CreateReleased(); Heap(); Check(!weak.IsAlive,"Released cache retained");
    Console.WriteLine($"PASS: 150,000 churn updates/deletes, retained delta {after-before} bytes; released cache collected"); return;
}
var baseline=Heap(); var memory=new SearchMemoryCache(); var clock=Stopwatch.StartNew();
using(var db=new IndexDatabase(args[0],true))
{
    db.Execute("PRAGMA cache_size=-4096");
    using(var size=db.Prepare("SELECT count(*) FROM entries")){size.Step();memory.Reserve((int)size.Number(0));}
    using(var rows=db.Prepare("SELECT rowid,search,initials,folder FROM entries"))
        while(rows.Step()) memory.Put(rows.Number(0),rows.Text(1),rows.Text(2),rows.Number(3)!=0);
}
clock.Stop(); var loadMs=clock.Elapsed.TotalMilliseconds; var heap=Heap();
var metrics=new List<object>();
foreach(bool parents in new[]{false,true}) foreach(var query in new[]{"문서","ㄱ","여행","보고서","(사진 OR 문서) !임시","Mycelview_NoMatch_938175"})
{
    var options=new SearchOptions{IncludeParentFolders=parents};var plan=SearchQuery.Compile(query,options);
    memory.Find(plan,201,default,options);
    var times=new List<double>();List<long> ids=[];long allocated=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<5;i++){clock.Restart();ids=memory.Find(plan,201,default,options);times.Add(clock.Elapsed.TotalMilliseconds);}
    allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
    times.Sort();metrics.Add(new{parents,query,count=ids.Count,hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(',',ids)))),medianMs=times[2],allocatedPerQuery=allocated/5});
}
var process=Process.GetCurrentProcess();process.Refresh();
File.WriteAllText(args[1],JsonSerializer.Serialize(new{loadMs,cacheHeapMiB=(heap-baseline)/1048576.0,heapMiB=heap/1048576.0,workingSetMiB=process.WorkingSet64/1048576.0,privateMiB=process.PrivateMemorySize64/1048576.0,metrics},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(File.ReadAllText(args[1]));
GC.KeepAlive(memory);
