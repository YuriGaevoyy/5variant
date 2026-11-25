using System.Globalization;
using System.IO;

namespace DeliveryService.Desktop;

/// <summary>Клиентский журнал: INFO/WARN/ERROR, ротация по дням, удаление файлов старше 14 дней.</summary>
public static class ClientLog
{
    private static readonly object Gate = new();
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeliveryService", "logs");

    public static void Start()
    {
        Directory.CreateDirectory(DirectoryPath);
        foreach (string file in Directory.GetFiles(DirectoryPath, "client-*.log"))
        {
            if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                File.Delete(file);
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);

    public static void Write(Exception error, string? context = null)
    {
        Write("ERROR", $"{context}: {error}");
    }

    private static void Write(string level, string message)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string path = Path.Combine(DirectoryPath, $"client-{DateTime.Now:yyyy-MM-dd}.log");
        lock (Gate)
            File.AppendAllText(path, $"{stamp} [{level}] {message}{Environment.NewLine}");
    }
}
