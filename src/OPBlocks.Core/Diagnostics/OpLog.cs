using System;
using System.IO;
using System.Text;

namespace OPBlocks.Core.Diagnostics
{
    /// <summary>
    /// File logger for the zero-error robustness contract (spec §8.8): every
    /// unexpected exception at the COM boundary is logged to
    /// <c>%LOCALAPPDATA%\OPBlocks\logs\</c> and the log path is surfaced to the
    /// user in the resulting <see cref="CapeOpen.CapeUnknownException"/> message.
    ///
    /// Logging must never itself throw across the COM boundary, so every method
    /// here swallows its own I/O errors and degrades to returning a path string.
    /// </summary>
    public static class OpLog
    {
        private static readonly object Gate = new object();

        /// <summary>Root log directory, created on demand.</summary>
        public static string LogDirectory
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "OPBlocks", "logs");
            }
        }

        private static string LogFilePath
        {
            get { return Path.Combine(LogDirectory, "opblocks-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        /// <summary>Writes an informational line. Never throws.</summary>
        public static void Info(string block, string message)
        {
            Write("INFO ", block, message, null);
        }

        /// <summary>
        /// Logs an exception with full detail and returns the log file path so the
        /// caller can embed it in the user-facing error message.
        /// </summary>
        public static string Error(string block, string context, Exception ex)
        {
            Write("ERROR", block, context, ex);
            try { return LogFilePath; }
            catch { return LogDirectory; }
        }

        private static void Write(string level, string block, string message, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append("  ").Append(level);
                sb.Append("  [").Append(block ?? "?").Append("]  ");
                sb.Append(message);
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append("    ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                        sb.AppendLine().Append(ex.StackTrace);
                    if (ex.InnerException != null)
                        sb.AppendLine().Append("    inner: ").Append(ex.InnerException);
                }

                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogFilePath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging is best-effort; never let it break a calculation.
            }
        }
    }
}
