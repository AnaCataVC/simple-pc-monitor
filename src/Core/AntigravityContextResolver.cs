using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SystemCoreMonitor.Core
{
    public static class AntigravityContextResolver
    {
        private static readonly object SyncLock = new object();
        private static string _cachedContext = string.Empty;
        private static string _cachedWorkspace = string.Empty;
        private static DateTime _lastResolveTime = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);

        private static class WinSqlite
        {
            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_step(IntPtr pStmt);

            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr sqlite3_column_text(IntPtr pStmt, int iCol);

            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_finalize(IntPtr pStmt);

            [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_close(IntPtr db);

            public const int SQLITE_OPEN_READONLY = 0x00000001;
            public const int SQLITE_ROW = 100;
        }

        public static (string Context, string Workspace) ResolveLatestSession()
        {
            lock (SyncLock)
            {
                if (DateTime.UtcNow - _lastResolveTime < CacheTtl && !string.IsNullOrEmpty(_cachedContext))
                {
                    return (_cachedContext, _cachedWorkspace);
                }

                try
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string dbPath = Path.Combine(userProfile, ".gemini", "antigravity", "conversation_summaries.db");

                    if (File.Exists(dbPath))
                    {
                        var info = QueryLatestConversation(dbPath);
                        if (!string.IsNullOrEmpty(info.Workspace) || !string.IsNullOrEmpty(info.Title))
                        {
                            string context = FormatContext(info.Workspace, info.Title);
                            _cachedContext = context;
                            _cachedWorkspace = info.Workspace;
                            _lastResolveTime = DateTime.UtcNow;
                            return (context, info.Workspace);
                        }
                    }

                    // Fallback to Antigravity IDE workspaceStorage
                    string ideWorkspace = ResolveAntigravityIdeWorkspace();
                    if (!string.IsNullOrEmpty(ideWorkspace))
                    {
                        string context = "📁 " + ideWorkspace;
                        _cachedContext = context;
                        _cachedWorkspace = ideWorkspace;
                        _lastResolveTime = DateTime.UtcNow;
                        return (context, ideWorkspace);
                    }
                }
                catch
                {
                    // Fail closed without throwing into metric collector
                }

                return ("⚡ Antigravity Workspace", string.Empty);
            }
        }

        private static (string Title, string Workspace) QueryLatestConversation(string dbPath)
        {
            IntPtr db = IntPtr.Zero;
            try
            {
                byte[] dbBytes = Encoding.UTF8.GetBytes(dbPath + "\0");
                int rc = WinSqlite.sqlite3_open_v2(dbBytes, out db, WinSqlite.SQLITE_OPEN_READONLY, IntPtr.Zero);
                if (rc != 0 || db == IntPtr.Zero) return (string.Empty, string.Empty);

                string sql = "SELECT title, workspace_uris FROM conversation_summaries ORDER BY last_modified_time DESC LIMIT 1\0";
                byte[] sqlBytes = Encoding.UTF8.GetBytes(sql);
                IntPtr stmt = IntPtr.Zero;

                rc = WinSqlite.sqlite3_prepare_v2(db, sqlBytes, sqlBytes.Length, out stmt, IntPtr.Zero);
                if (rc == 0 && stmt != IntPtr.Zero)
                {
                    try
                    {
                        if (WinSqlite.sqlite3_step(stmt) == WinSqlite.SQLITE_ROW)
                        {
                            IntPtr titlePtr = WinSqlite.sqlite3_column_text(stmt, 0);
                            IntPtr wsPtr = WinSqlite.sqlite3_column_text(stmt, 1);

                            string rawTitle = titlePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(titlePtr) ?? string.Empty : string.Empty;
                            string rawWs = wsPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(wsPtr) ?? string.Empty : string.Empty;

                            string cleanWs = ExtractWorkspaceFromUri(rawWs);
                            return (rawTitle.Trim(), cleanWs);
                        }
                    }
                    finally
                    {
                        WinSqlite.sqlite3_finalize(stmt);
                    }
                }
            }
            catch
            {
                // Native SQLite query exception handling
            }
            finally
            {
                if (db != IntPtr.Zero)
                {
                    WinSqlite.sqlite3_close(db);
                }
            }

            return (string.Empty, string.Empty);
        }

        public static string ExtractWorkspaceFromUri(string rawUriJson)
        {
            if (string.IsNullOrWhiteSpace(rawUriJson)) return string.Empty;

            try
            {
                // Match URI like "file:///c%3A/Users/anaca/Repos/system-core-monitor" or path
                var match = Regex.Match(rawUriJson, @"file:///[^""'\]]+");
                string path = match.Success ? match.Value : rawUriJson;

                path = Uri.UnescapeDataString(path);
                path = path.Replace("file:///", string.Empty).Replace('/', '\\');

                // Path privacy: extract repository folder name
                string folder = Path.GetFileName(path.TrimEnd('\\'));
                if (!string.IsNullOrEmpty(folder))
                {
                    return folder;
                }
            }
            catch { }

            return string.Empty;
        }

        private static string ResolveAntigravityIdeWorkspace()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string storageDir = Path.Combine(appData, "Antigravity IDE", "User", "workspaceStorage");
                if (!Directory.Exists(storageDir)) return string.Empty;

                DirectoryInfo? mostRecentDir = null;
                DateTime maxTime = DateTime.MinValue;

                foreach (var dirPath in Directory.GetDirectories(storageDir))
                {
                    var di = new DirectoryInfo(dirPath);
                    if (di.LastWriteTimeUtc > maxTime)
                    {
                        maxTime = di.LastWriteTimeUtc;
                        mostRecentDir = di;
                    }
                }

                if (mostRecentDir != null)
                {
                    string wsFile = Path.Combine(mostRecentDir.FullName, "workspace.json");
                    if (File.Exists(wsFile))
                    {
                        string json = File.ReadAllText(wsFile);
                        return ExtractWorkspaceFromUri(json);
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        public static string FormatContext(string workspace, string title)
        {
            if (!string.IsNullOrEmpty(workspace) && !string.IsNullOrEmpty(title))
            {
                return string.Format("📁 {0} • {1}", workspace, title);
            }
            if (!string.IsNullOrEmpty(workspace))
            {
                return string.Format("📁 {0}", workspace);
            }
            if (!string.IsNullOrEmpty(title))
            {
                return string.Format("⚡ {0}", title);
            }
            return "⚡ Antigravity Workspace";
        }
    }
}
