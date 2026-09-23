using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Tidbok.Data;

// Ett tunt lager direkt mot SQLite:s C-API. Det finns av ett praktiskt skäl: miljön projektet
// byggdes i nådde inte NuGet, så Microsoft.Data.Sqlite gick inte att hämta. Lagret täcker exakt
// det Tidbok behöver: öppna, förbereda, binda parametrar, stega och läsa kolumner. All SQL i
// projektet går genom parametrar, aldrig genom strängbygge.
//
// Biblioteket som laddas är systemets egen SQLite: libsqlite3 på Linux och winsqlite3.dll som
// följer med Windows 10 och 11. Docker-avbildningen installerar libsqlite3-0.

internal static class Native
{
    private const string Lib = "sqlite3";

    public const int OK = 0, ROW = 100, DONE = 101;
    public const int INTEGER = 1, FLOAT = 2, TEXT = 3, BLOB = 4, NULL = 5;
    public const int OPEN_READWRITE = 0x2, OPEN_CREATE = 0x4, OPEN_FULLMUTEX = 0x10000;
    public static readonly IntPtr TRANSIENT = new(-1);

    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    }

    public static void EnsureLoaded() { /* kör den statiska konstruktorn */ }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib) return IntPtr.Zero;
        string[] candidates = OperatingSystem.IsWindows()
            ? ["winsqlite3", "sqlite3"]
            : OperatingSystem.IsMacOS()
                ? ["libsqlite3.dylib", "libsqlite3"]
                : ["libsqlite3.so.0", "libsqlite3.so", "libsqlite3"];

        foreach (var c in candidates)
            if (NativeLibrary.TryLoad(c, assembly, path, out var handle))
                return handle;

        throw new DllNotFoundException(
            "Hittar inte SQLite. På Linux: installera paketet libsqlite3-0. På Windows ska winsqlite3.dll finnas i System32.");
    }

    [DllImport(Lib)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib)] public static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Lib)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport(Lib)] public static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);
    [DllImport(Lib)] public static extern void sqlite3_free(IntPtr p);
    [DllImport(Lib)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nbyte, out IntPtr stmt, out IntPtr tail);
    [DllImport(Lib)] public static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib)] public static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib)] public static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
    [DllImport(Lib)] public static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);
    [DllImport(Lib)] public static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int nbytes, IntPtr destructor);
    [DllImport(Lib)] public static extern int sqlite3_bind_null(IntPtr stmt, int index);
    [DllImport(Lib)] public static extern int sqlite3_bind_parameter_count(IntPtr stmt);
    [DllImport(Lib)] public static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport(Lib)] public static extern int sqlite3_column_type(IntPtr stmt, int col);
    [DllImport(Lib)] public static extern long sqlite3_column_int64(IntPtr stmt, int col);
    [DllImport(Lib)] public static extern double sqlite3_column_double(IntPtr stmt, int col);
    [DllImport(Lib)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
    [DllImport(Lib)] public static extern int sqlite3_column_bytes(IntPtr stmt, int col);
    [DllImport(Lib)] public static extern int sqlite3_changes(IntPtr db);
    [DllImport(Lib)] public static extern long sqlite3_last_insert_rowid(IntPtr db);

    public static byte[] Utf8Z(string s)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
        return bytes;
    }
}

public sealed class SqliteException(int code, string message) : Exception($"SQLite {code}: {message}")
{
    public int Code { get; } = code;
}

/// <summary>En öppen anslutning. Används av en tråd i taget och stängs efter varje arbetsmoment.</summary>
public sealed class SqliteConnection : IDisposable
{
    private IntPtr _db;

    public SqliteConnection(string path)
    {
        Native.EnsureLoaded();
        int rc = Native.sqlite3_open_v2(Native.Utf8Z(path), out _db,
            Native.OPEN_READWRITE | Native.OPEN_CREATE | Native.OPEN_FULLMUTEX, IntPtr.Zero);
        if (rc != Native.OK)
        {
            var msg = _db != IntPtr.Zero ? Error() : "kunde inte öppna databasen";
            Native.sqlite3_close_v2(_db);
            throw new SqliteException(rc, msg);
        }
        Native.sqlite3_busy_timeout(_db, 5000);
        ExecuteScript("PRAGMA foreign_keys = ON;");
    }

    /// <summary>Kör flera satser utan parametrar, till exempel schemat.</summary>
    public void ExecuteScript(string sql)
    {
        int rc = Native.sqlite3_exec(_db, Native.Utf8Z(sql), IntPtr.Zero, IntPtr.Zero, out var err);
        if (rc != Native.OK)
        {
            var msg = err != IntPtr.Zero ? Marshal.PtrToStringUTF8(err) ?? "" : Error();
            if (err != IntPtr.Zero) Native.sqlite3_free(err);
            throw new SqliteException(rc, msg);
        }
    }

    /// <summary>Kör en sats med parametrar (?) och returnerar antal ändrade rader.</summary>
    public int Execute(string sql, params object?[] args)
    {
        using var stmt = Prepare(sql, args);
        int rc;
        while ((rc = Native.sqlite3_step(stmt.Handle)) == Native.ROW) { }
        if (rc != Native.DONE) throw new SqliteException(rc, Error());
        return Native.sqlite3_changes(_db);
    }

    public long Insert(string sql, params object?[] args)
    {
        Execute(sql, args);
        return Native.sqlite3_last_insert_rowid(_db);
    }

    public List<T> Query<T>(string sql, Func<Row, T> map, params object?[] args)
    {
        using var stmt = Prepare(sql, args);
        var result = new List<T>();
        var row = new Row(stmt.Handle);
        int rc;
        while ((rc = Native.sqlite3_step(stmt.Handle)) == Native.ROW) result.Add(map(row));
        if (rc != Native.DONE) throw new SqliteException(rc, Error());
        return result;
    }

    public T? Single<T>(string sql, Func<Row, T> map, params object?[] args) =>
        Query(sql, map, args).FirstOrDefault();

    public long Scalar(string sql, params object?[] args) =>
        Query(sql, r => r.Long(0), args).FirstOrDefault();

    /// <summary>
    /// Transaktion. <c>BEGIN IMMEDIATE</c> tar skrivlåset direkt, så två samtidiga bokningar
    /// ställer sig i kö i stället för att båda läsa "ledigt" och sedan båda skriva.
    /// </summary>
    public T InTransaction<T>(Func<T> work, bool immediate = true)
    {
        ExecuteScript(immediate ? "BEGIN IMMEDIATE;" : "BEGIN;");
        try
        {
            var result = work();
            ExecuteScript("COMMIT;");
            return result;
        }
        catch
        {
            try { ExecuteScript("ROLLBACK;"); } catch { /* ursprungsfelet är det viktiga */ }
            throw;
        }
    }

    private Statement Prepare(string sql, object?[] args)
    {
        var bytes = Native.Utf8Z(sql);
        int rc = Native.sqlite3_prepare_v2(_db, bytes, bytes.Length, out var handle, out _);
        if (rc != Native.OK) throw new SqliteException(rc, Error() + " i: " + sql);

        var stmt = new Statement(handle);
        int expected = Native.sqlite3_bind_parameter_count(handle);
        if (expected != args.Length)
        {
            stmt.Dispose();
            throw new ArgumentException($"Satsen vill ha {expected} parametrar men fick {args.Length}: {sql}");
        }

        for (int i = 0; i < args.Length; i++)
        {
            int idx = i + 1;
            rc = args[i] switch
            {
                null => Native.sqlite3_bind_null(handle, idx),
                int v => Native.sqlite3_bind_int64(handle, idx, v),
                long v => Native.sqlite3_bind_int64(handle, idx, v),
                bool v => Native.sqlite3_bind_int64(handle, idx, v ? 1 : 0),
                double v => Native.sqlite3_bind_double(handle, idx, v),
                string v => BindText(handle, idx, v),
                DateTime v => BindText(handle, idx, Db.FormatTime(v)),
                DateOnly v => BindText(handle, idx, Db.FormatDate(v)),
                Enum v => BindText(handle, idx, v.ToString()),
                var other => throw new ArgumentException($"Typen {other.GetType().Name} kan inte bindas.")
            };
            if (rc != Native.OK)
            {
                stmt.Dispose();
                throw new SqliteException(rc, Error());
            }
        }
        return stmt;
    }

    private static int BindText(IntPtr stmt, int idx, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Native.sqlite3_bind_text(stmt, idx, bytes, bytes.Length, Native.TRANSIENT);
    }

    private string Error() => Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)) ?? "okänt fel";

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close_v2(_db);
            _db = IntPtr.Zero;
        }
    }

    private sealed class Statement(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; private set; } = handle;
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) Native.sqlite3_finalize(Handle);
            Handle = IntPtr.Zero;
        }
    }
}

/// <summary>Aktuell rad under en läsning. Kolumner läses med index i den ordning SELECT listar dem.</summary>
public sealed class Row(IntPtr stmt)
{
    public bool IsNull(int col) => Native.sqlite3_column_type(stmt, col) == Native.NULL;
    public long Long(int col) => Native.sqlite3_column_int64(stmt, col);
    public int Int(int col) => (int)Native.sqlite3_column_int64(stmt, col);
    public bool Bool(int col) => Native.sqlite3_column_int64(stmt, col) != 0;
    public int? IntOrNull(int col) => IsNull(col) ? null : Int(col);

    public string Text(int col)
    {
        var ptr = Native.sqlite3_column_text(stmt, col);
        if (ptr == IntPtr.Zero) return "";
        int len = Native.sqlite3_column_bytes(stmt, col);
        return Marshal.PtrToStringUTF8(ptr, len);
    }

    public string? TextOrNull(int col) => IsNull(col) ? null : Text(col);
    public DateTime Time(int col) => Db.ParseTime(Text(col));
    public DateTime? TimeOrNull(int col) => IsNull(col) ? null : Db.ParseTime(Text(col));
}
