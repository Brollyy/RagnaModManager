using System.Runtime.InteropServices;

namespace RagnaModManager.Core.Database;

internal sealed class SqliteConnection : IDisposable
{
    private IntPtr _db;
    private static bool _resolverRegistered;

    static SqliteConnection()
    {
        NativeLibrary.SetDllImportResolver(typeof(SqliteConnection).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName != "sqlite3")
            {
                return IntPtr.Zero;
            }

            var candidates = OperatingSystem.IsWindows()
                ? ["sqlite3.dll"]
                : OperatingSystem.IsMacOS()
                    ? ["libsqlite3.dylib", "/usr/lib/libsqlite3.dylib"]
                    : new[] { "libsqlite3.so.0", "libsqlite3.so" };

            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        });
        _resolverRegistered = true;
    }

    public SqliteConnection(string path)
    {
        _ = _resolverRegistered;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rc = Native.sqlite3_open(path, out _db);
        if (rc != 0)
        {
            throw new InvalidOperationException($"Could not open SQLite database: {LastError()}");
        }
    }

    public void Execute(string sql)
    {
        var rc = Native.sqlite3_exec(_db, sql, null, IntPtr.Zero, out var err);
        if (rc != 0)
        {
            var message = err == IntPtr.Zero ? LastError() : Marshal.PtrToStringUTF8(err) ?? "unknown SQLite error";
            if (err != IntPtr.Zero)
            {
                Native.sqlite3_free(err);
            }

            throw new InvalidOperationException(message);
        }
    }

    public List<Dictionary<string, string?>> Query(string sql)
    {
        var rows = new List<Dictionary<string, string?>>();
        Native.Callback callback = (_, columnCount, values, names) =>
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columnCount; i++)
            {
                var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(names, i * IntPtr.Size)) ?? $"column{i}";
                var valuePtr = Marshal.ReadIntPtr(values, i * IntPtr.Size);
                row[name] = valuePtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(valuePtr);
            }

            rows.Add(row);
            return 0;
        };

        var rc = Native.sqlite3_exec(_db, sql, callback, IntPtr.Zero, out var err);
        if (rc != 0)
        {
            var message = err == IntPtr.Zero ? LastError() : Marshal.PtrToStringUTF8(err) ?? "unknown SQLite error";
            if (err != IntPtr.Zero)
            {
                Native.sqlite3_free(err);
            }

            throw new InvalidOperationException(message);
        }

        GC.KeepAlive(callback);
        return rows;
    }

    public static string Quote(string? value) => value is null ? "NULL" : "'" + value.Replace("'", "''") + "'";

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    private string LastError() => Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)) ?? "unknown SQLite error";

    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Callback(IntPtr data, int columnCount, IntPtr values, IntPtr names);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_open(string filename, out IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_close(IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_exec(IntPtr db, string sql, Callback? callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        public static extern void sqlite3_free(IntPtr ptr);
    }
}
