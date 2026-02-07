using System.Text.RegularExpressions;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Parses adb logcat output in threadtime format into structured LogcatEntry records.
/// Format: MM-DD HH:MM:SS.mmm  PID  TID  LEVEL  TAG: MESSAGE
/// </summary>
public static partial class LogcatParser
{
    // Regex for threadtime format: "01-01 12:00:01.234  1234  5678 I ActivityManager: Start proc"
    [GeneratedRegex(@"^(\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})\s+(\d+)\s+(\d+)\s+([VDIWEFA])\s+(.+?):\s(.*)$")]
    private static partial Regex ThreadtimeRegex();

    public static LogcatEntry? Parse(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var match = ThreadtimeRegex().Match(line);
        if (!match.Success)
            return null;

        var level = match.Groups[4].Value[0] switch
        {
            'V' => LogcatLevel.Verbose,
            'D' => LogcatLevel.Debug,
            'I' => LogcatLevel.Info,
            'W' => LogcatLevel.Warning,
            'E' => LogcatLevel.Error,
            'F' or 'A' => LogcatLevel.Fatal,
            _ => LogcatLevel.Verbose,
        };

        return new LogcatEntry(
            Timestamp: match.Groups[1].Value,
            Pid: int.Parse(match.Groups[2].Value),
            Tid: int.Parse(match.Groups[3].Value),
            Level: level,
            Tag: match.Groups[5].Value.Trim(),
            Message: match.Groups[6].Value,
            RawLine: line
        );
    }

    /// <summary>
    /// Creates a continuation entry for lines that don't match threadtime format
    /// (e.g. multi-line stack traces). Inherits metadata from the previous entry.
    /// </summary>
    public static LogcatEntry CreateContinuation(string line, LogcatEntry? previous)
    {
        return new LogcatEntry(
            Timestamp: previous?.Timestamp ?? "",
            Pid: previous?.Pid ?? 0,
            Tid: previous?.Tid ?? 0,
            Level: previous?.Level ?? LogcatLevel.Verbose,
            Tag: previous?.Tag ?? "",
            Message: line,
            RawLine: line
        );
    }
}

public enum LogcatLevel
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}

public record LogcatEntry(
    string Timestamp,
    int Pid,
    int Tid,
    LogcatLevel Level,
    string Tag,
    string Message,
    string RawLine
);
