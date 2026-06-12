namespace RagnaModManager.Core.Logging;

public sealed class AppLogger
{
    private readonly string _logDirectory;

    public AppLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("app.log", "INFO", message);

    public void Error(string message) => Write("app.log", "ERROR", message);

    public void Deployment(string message) => Write("deployment.log", "INFO", message);

    private void Write(string fileName, string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(_logDirectory, fileName), line);
    }
}
