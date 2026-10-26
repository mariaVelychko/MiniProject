using System;
using System.IO;
using System.Threading.Tasks;
using SportClubLibrary.Interfaces;

namespace SportClubLibrary.Utilities
{
    public enum LogType
    {
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    public class FileLogger : ILogger
    {
        private readonly string _logPath;

        public FileLogger()
        {
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
        }

        public void Log(string message, LogType type = LogType.Info)
        {
            try
            {
                string logEntry = $"[{DateTime.Now}] [{type.ToString().ToUpper()}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, logEntry);
            }
            catch { }
        }

        public async Task LogAsync(string message, LogType type = LogType.Info)
        {
            try
            {
                string logEntry = $"[{DateTime.Now}] [{type.ToString().ToUpper()}] {message}{Environment.NewLine}";
                await File.AppendAllTextAsync(_logPath, logEntry);
            }
            catch { }
        }
    }
}
