using System.Runtime.InteropServices;

namespace PinShelf;

// Windows 10/11 ships SQLite. No external packages or background service required.
internal sealed class IndexDatabase : IDisposable
{
    private IntPtr db;
    public IndexDatabase(string path, bool readOnly = false)
    {
        var rc = sqlite3_open_v2(path, out db, (readOnly ? 1 : 2 | 4) | 0x10000, IntPtr.Zero);
        if (rc != 0) { var message = Error; Dispose(); throw new InvalidOperationException(message); }
        sqlite3_busy_timeout(db, 5000);
    }
    private string Error => Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "SQLite 오류";
    public void Execute(string sql)
    { if (sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error) != 0) { var message = Marshal.PtrToStringUTF8(error) ?? Error; sqlite3_free(error); throw new InvalidOperationException(message); } }
    public Statement Prepare(string sql) => new(this, sql);
    public void Interrupt() { if (db != IntPtr.Zero) sqlite3_interrupt(db); }
    public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close_v2(db); db = IntPtr.Zero; } }
    internal sealed class Statement : IDisposable
    {
        private readonly IndexDatabase owner;
        private IntPtr statement;
        public Statement(IndexDatabase owner, string sql)
        { this.owner = owner; if (sqlite3_prepare_v2(owner.db, sql, -1, out statement, IntPtr.Zero) != 0) throw new InvalidOperationException(owner.Error); }
        public void Bind(int index, string value) { if (sqlite3_bind_text(statement, index, value, -1, new IntPtr(-1)) != 0) throw new InvalidOperationException(owner.Error); }
        public void Bind(int index, long value) { if (sqlite3_bind_int64(statement, index, value) != 0) throw new InvalidOperationException(owner.Error); }
        public bool Step()
        { var rc = sqlite3_step(statement); if (rc == 100) return true; if (rc == 101) return false; if (rc == 9) throw new OperationCanceledException(); throw new InvalidOperationException(owner.Error); }
        public string Text(int i) => Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)) ?? "";
        public long Number(int i) => sqlite3_column_int64(statement, i);
        public void Reset() { sqlite3_reset(statement); sqlite3_clear_bindings(statement); }
        public void Dispose() { if (statement != IntPtr.Zero) { sqlite3_finalize(statement); statement = IntPtr.Zero; } }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr ptr);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_interrupt(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr statement, int i, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int bytes, IntPtr destroy);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr statement, int i, long value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int i);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int i);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_reset(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_clear_bindings(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
}
