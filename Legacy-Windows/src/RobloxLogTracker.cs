using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RoMulti
{
    public sealed class RobloxServerInfo
    {
        internal const string CloverLibraryPlaceId = "14891672511";

        public string PlaceId { get; set; }
        public string JobId { get; set; }
        public string LogFileName { get; set; }
        public DateTime LastJoinUtc { get; set; }
        public bool IsDisconnected { get; set; }

        public bool HasServer
        {
            get { return !IsDisconnected && !string.IsNullOrEmpty(PlaceId) && !string.IsNullOrEmpty(JobId); }
        }

        public string DirectJoinUri
        {
            get
            {
                if (!HasServer) return string.Empty;
                return string.Format("roblox://experiences/start?placeId={0}&gameInstanceId={1}", PlaceId, JobId);
            }
        }
    }

    public sealed class RobloxLogTracker
    {
        private static readonly Regex LogTimestampRegex = new Regex(
            @"_(\d{8}T\d{6}Z)_Player_",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex JoiningGameRegex = new Regex(
            @"! Joining game '([0-9a-fA-F\-]+)' place (\d+)",
            RegexOptions.Compiled);

        private static readonly Regex ConnectionLostRegex = new Regex(
            @"\[FLog::Network\] Connection lost[^\S\r\n]*\r?$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private readonly object _stateLock = new object();
        private readonly Dictionary<int, string> _pidToLogPath = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _lastPlaceByPid = new Dictionary<int, string>();

        public RobloxServerInfo GetServerInfoForInstance(int pid, DateTime processStartLocal, out string previousPlaceId, bool updateTrackedPlace = true)
        {
            previousPlaceId = null;

            string logPath;
            lock (_stateLock)
                logPath = ResolveLogPath(pid, processStartLocal);
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                return null;

            var info = ParseLatestServerFromLog(logPath);
            if (info == null || string.IsNullOrEmpty(info.PlaceId))
                return info;

            lock (_stateLock)
            {
                string prev;
                if (_lastPlaceByPid.TryGetValue(pid, out prev) && !string.Equals(prev, info.PlaceId, StringComparison.Ordinal))
                    previousPlaceId = prev;
                if (updateTrackedPlace && _pidToLogPath.ContainsKey(pid))
                    _lastPlaceByPid[pid] = info.PlaceId;
            }
            return info;
        }

        public void PruneExitedPids(HashSet<int> activePids)
        {
            lock (_stateLock)
            {
                foreach (int pid in _pidToLogPath.Keys.Where(k => !activePids.Contains(k)).ToList())
                    _pidToLogPath.Remove(pid);
                foreach (int pid in _lastPlaceByPid.Keys.Where(k => !activePids.Contains(k)).ToList())
                    _lastPlaceByPid.Remove(pid);
            }
        }

        private string ResolveLogPath(int pid, DateTime processStartLocal)
        {
            string cached;
            if (_pidToLogPath.TryGetValue(pid, out cached) && File.Exists(cached))
                return cached;

            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                "logs");

            if (!Directory.Exists(logsDir))
                return null;

            DateTime startUtc = processStartLocal == DateTime.MinValue
                ? DateTime.UtcNow
                : processStartLocal.ToUniversalTime();

            var usedPaths = new HashSet<string>(_pidToLogPath.Values, StringComparer.OrdinalIgnoreCase);

            string bestPath = null;
            double bestDeltaSec = 90.0; // Allow up to 90s between process launch and log creation

            try
            {
                var files = Directory.GetFiles(logsDir, "*_Player_*_last.log");
                foreach (string f in files)
                {
                    if (usedPaths.Contains(f)) continue;
                    string name = Path.GetFileName(f);
                    if (name.IndexOf("CrashHandler", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    var m = LogTimestampRegex.Match(name);
                    if (!m.Success) continue;

                    DateTime logUtc;
                    if (!DateTime.TryParseExact(
                        m.Groups[1].Value,
                        "yyyyMMdd'T'HHmmss'Z'",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out logUtc))
                    {
                        continue;
                    }

                    double delta = Math.Abs((logUtc - startUtc).TotalSeconds);
                    if (delta < bestDeltaSec)
                    {
                        bestDeltaSec = delta;
                        bestPath = f;
                    }
                }
            }
            catch
            {
                return null;
            }

            if (!string.IsNullOrEmpty(bestPath))
                _pidToLogPath[pid] = bestPath;

            return bestPath;
        }

        public static RobloxServerInfo ParseLatestServerFromLog(string logPath)
        {
            try
            {
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // Read last 512 KB for speed on huge multi-hour logs
                    long maxTail = 512 * 1024;
                    if (fs.Length > maxTail)
                        fs.Seek(-maxTail, SeekOrigin.End);

                    string tailText;
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true, 8192, leaveOpen: true))
                    {
                        tailText = sr.ReadToEnd();
                    }

                    var matches = JoiningGameRegex.Matches(tailText);
                    if (matches.Count == 0 && fs.Length > maxTail)
                    {
                        // Fallback: read from beginning if player hasn't teleported in hours
                        fs.Seek(0, SeekOrigin.Begin);
                        using (var sr = new StreamReader(fs, Encoding.UTF8, true, 8192, leaveOpen: true))
                        {
                            tailText = sr.ReadToEnd();
                        }
                        matches = JoiningGameRegex.Matches(tailText);
                    }

                    if (matches.Count == 0)
                    {
                        return new RobloxServerInfo { LogFileName = Path.GetFileName(logPath) };
                    }

                    Match last = matches[matches.Count - 1];
                    return new RobloxServerInfo
                    {
                        JobId = last.Groups[1].Value,
                        PlaceId = last.Groups[2].Value,
                        IsDisconnected = ConnectionLostRegex.IsMatch(tailText, last.Index + last.Length),
                        LogFileName = Path.GetFileName(logPath),
                        LastJoinUtc = File.GetLastWriteTimeUtc(logPath)
                    };
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
