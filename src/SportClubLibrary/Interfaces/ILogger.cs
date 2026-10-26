using SportClubLibrary.Utilities;

public interface ILogger
{
    void Log(string message, LogType type = LogType.Info);
    Task LogAsync(string message, LogType type = LogType.Info);
}
