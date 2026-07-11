using System.Diagnostics;
using SPTarkov.Server.Core.Models.Utils;

namespace TheQuartermaster.Server.Services;

public static class LoggerExtensions
{
    /// <summary>
    ///     Logs an Info message only when compiled with the DEBUG flag.
    ///     Used for non-essential logs that should not appear in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void DebugInfo<T>(this ISptLogger<T> logger, string message)
    {
        logger.Info(message);
    }

    /// <summary>
    ///     Logs a Debug message only when compiled with the DEBUG flag.
    ///     Used for verbose logs that should not appear in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void DebugDebug<T>(this ISptLogger<T> logger, string message)
    {
        logger.Debug(message);
    }

    /// <summary>
    ///     Logs a Warning message only when compiled with the DEBUG flag.
    ///     Used for warnings that should not appear in release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void DebugWarning<T>(this ISptLogger<T> logger, string message)
    {
        logger.Warning(message);
    }
}
