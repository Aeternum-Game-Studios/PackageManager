using UnityEngine;

namespace Aeternum.Packages
{
    /// <summary>Console messages, without stack traces: they're read by people and in batch-mode logs.</summary>
    internal static class Logs
    {
        const string Prefix = "[AGS Packages] ";

        public static void Info(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", Prefix + message);

        public static void Warning(string message) =>
            Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "{0}", Prefix + message);

        public static void Error(string message) =>
            Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, null, "{0}", Prefix + message);
    }
}
