# Memory and folder interaction validation — 2026-11-13

All checks ran without computer-use tools. No real user files were copied, moved or deleted. The real search database was opened read-only. Benchmarks ran at BelowNormal process priority.

## Search memory

Same 1,302,191-entry database, sequential old/new console processes, five timed samples after warmup per query. These are search-cache measurements, not the total GUI process.

| Measurement | Before | After |
| --- | ---: | ---: |
| Retained managed cache | 501.1 MiB | 274.0 MiB |
| Benchmark working set | 583.4 MiB | 322.7 MiB |
| Cache loading | 5.56 s | 3.97 s |
| Default name-search median range (six queries) | 55–165 ms | 27–118 ms |
| Per-query managed allocations (same samples) | 59–213 MiB | 1.7 KiB |

All 12 before/after result ID hashes matched. Parent-path-inclusive queries ranged from 46–374 ms after versus 52–345 ms before: some improved, two regressed approximately 8–13%. This preserves the full in-memory index rather than reducing indexed drives or truncating results.

Implementation shares directory prefixes across siblings, shares identical initial-consonant strings, matches spans without per-record path substrings, and returns pooled query buffers. Missing parent records no longer prevent removal of descendant cache entries. Initial-consonant generation avoids a temporary character array. The extension icon cache is bounded.

The SearchMemory tests passed 288 combinations covering exact/partial names, extension removal, initial consonants, parent matching, target types and compound expressions. They also check cancellation, subtree deletion, slot reuse and replacement. After 150,000 repeated additions/deletions, retained heap growth was 0 bytes in this run; an unreferenced cache was collected. This is evidence for cache lifetime correctness, not a claim that every possible GUI/native leak has been ruled out.

## Folder interactions

- Selected rows use a one-pixel blue outline with the existing light-blue fill. Explicit RowStyle assignment avoids Fluent's overriding row style.
- Images snap to layout/device pixels; HICON bitmap dimensions are preserved instead of forcing a 16-pixel intermediate bitmap.
- Empty-area marquee selection supports reverse direction, Ctrl/Shift additive selection, edge auto-scroll, capture loss/unload cleanup and Escape cancellation. Existing item drag/drop remains on rows.
- Background menus obtain the real folder IContextMenu via IShellFolder.CreateViewObject. Shell-owned New, Properties and installed extensions remain native. Mycelview adds folder registration and Explorer opening.
- Explorer host commands (view, sort, group, refresh, paste, terminal and undo) are connected to Mycelview. Subsequent work added seven icon/list/details/tile modes for folder cards; see WINDOWS-EXPLORER-DIFFERENCES.md. Grouping supports type/extension in Details mode.
- Hidden WPF checks validate the selected row style, native background menu creation, sorting, grouping, column visibility and rectangle intersection/reverse/scroll calculations. The rendered selection was visually inspected.
- Existing shell icon tests passed individual shortcuts, desktop system items, alias deduplication, cache reuse and fallback. No target applications launched.

Physical mouse tests and a long-running production GUI memory soak were not performed, honoring the user's request to work in the background.
Production executable was rebuilt with zero warnings/errors and restarted normally. Early post-restart working set was about 500 MiB (private 424 MiB), versus approximately 1,032 MiB before replacement; startup/reconciliation timing differs, so this is a snapshot rather than a controlled GUI benchmark.
