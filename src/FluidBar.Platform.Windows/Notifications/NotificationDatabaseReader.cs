using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace FluidBar.Platform.Windows.Notifications;

/// <summary>
/// Read-only compatibility reader for the Windows notification database. This keeps unpackaged Win32
/// builds useful on systems where UserNotificationListener requires package identity.
/// </summary>
internal sealed class NotificationDatabaseReader
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteReadOnly = 1;
    private readonly int _maxTextCharacters;
    private long _watermark;

    internal NotificationDatabaseReader(int maxTextCharacters)
    {
        _maxTextCharacters = Math.Max(32, maxTextCharacters);
    }

    internal static string DatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows", "Notifications", "wpndatabase.db");

    internal bool TryPrime()
    {
        if (!TryOpen(out var db))
        {
            return false;
        }

        try
        {
            _watermark = ReadMaximumOrder(db);
            return true;
        }
        finally
        {
            _ = sqlite3_close_v2(db);
        }
    }

    internal IReadOnlyList<NotificationCandidate> ReadNew()
    {
        if (!TryOpen(out var db))
        {
            return [];
        }

        var statement = IntPtr.Zero;
        try
        {
            const string sql = """
                SELECT n.[Order], n.Id, n.ArrivalTime, n.Payload, h.PrimaryId
                FROM Notification n
                JOIN NotificationHandler h ON h.RecordId = n.HandlerId
                WHERE n.Type = 'toast' AND n.[Order] > ?
                ORDER BY n.[Order] ASC
                LIMIT 50
                """;
            if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != SqliteOk
                || statement == IntPtr.Zero
                || sqlite3_bind_int64(statement, 1, _watermark) != SqliteOk)
            {
                return [];
            }

            var candidates = new List<NotificationCandidate>();
            var maximumOrder = _watermark;
            while (sqlite3_step(statement) == SqliteRow)
            {
                var order = sqlite3_column_int64(statement, 0);
                maximumOrder = Math.Max(maximumOrder, order);
                var id = unchecked((uint)sqlite3_column_int64(statement, 1));
                var arrivalTime = sqlite3_column_int64(statement, 2);
                var payload = ReadBlob(statement, 3);
                var primaryId = ReadText(statement, 4);
                var candidate = Parse(id, arrivalTime, payload, primaryId);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            _watermark = maximumOrder;
            return candidates;
        }
        finally
        {
            if (statement != IntPtr.Zero)
            {
                _ = sqlite3_finalize(statement);
            }

            _ = sqlite3_close_v2(db);
        }
    }

    private NotificationCandidate? Parse(uint id, long arrivalTime, string? payload, string? primaryId)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(payload);
            var texts = document.Descendants()
                .Where(element => element.Name.LocalName == "text")
                .Select(element => Truncate(element.Value.Trim()))
                .Where(text => text.Length > 0)
                .ToArray();
            if (texts.Length == 0)
            {
                return null;
            }

            var appName = FriendlyAppName(primaryId, payload);
            var title = texts[0];
            var body = Truncate(string.Join(Environment.NewLine, texts.Skip(1)));
            return new NotificationCandidate(id, appName, title, body, FromFileTime(arrivalTime));
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string FriendlyAppName(string? primaryId, string? payload = null)
    {
        var value = $"{primaryId} {payload}";
        if (ContainsAny(value, "wxwork", "wework", "wecom", "企业微信")) return "企业微信";
        if (ContainsAny(value, "wechat", "weixin", "微信")) return "微信";
        if (ContainsAny(value, "dingtalk", "ding talk", "钉钉")) return "钉钉";
        if (ContainsAny(value, "feishu", "lark", "飞书")) return "飞书";

        if (string.IsNullOrWhiteSpace(primaryId))
        {
            return "Windows 通知";
        }

        var normalized = primaryId.Trim();
        var separator = normalized.IndexOfAny(['!', '_']);
        return separator > 0 ? normalized[..separator] : normalized;
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private string Truncate(string value)
        => value.Length <= _maxTextCharacters ? value : value[.._maxTextCharacters];

    private static DateTimeOffset FromFileTime(long value)
    {
        try
        {
            return value > 0 ? DateTimeOffset.FromFileTime(value).ToLocalTime() : DateTimeOffset.Now;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.Now;
        }
    }

    private static long ReadMaximumOrder(IntPtr db)
    {
        var statement = IntPtr.Zero;
        try
        {
            if (sqlite3_prepare_v2(db, "SELECT COALESCE(MAX([Order]), 0) FROM Notification", -1,
                    out statement, IntPtr.Zero) != SqliteOk
                || statement == IntPtr.Zero
                || sqlite3_step(statement) != SqliteRow)
            {
                return 0;
            }

            return sqlite3_column_int64(statement, 0);
        }
        finally
        {
            if (statement != IntPtr.Zero)
            {
                _ = sqlite3_finalize(statement);
            }
        }
    }

    private static bool TryOpen(out IntPtr db)
    {
        db = IntPtr.Zero;
        return File.Exists(DatabasePath)
               && sqlite3_open_v2(DatabasePath, out db, SqliteReadOnly, IntPtr.Zero) == SqliteOk
               && db != IntPtr.Zero;
    }

    private static string? ReadBlob(IntPtr statement, int column)
    {
        var pointer = sqlite3_column_blob(statement, column);
        var length = sqlite3_column_bytes(statement, column);
        if (pointer == IntPtr.Zero || length <= 0)
        {
            return null;
        }

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string? ReadText(IntPtr statement, int column)
    {
        var pointer = sqlite3_column_text(statement, column);
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int numBytes, out IntPtr statement,
        IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);
}
