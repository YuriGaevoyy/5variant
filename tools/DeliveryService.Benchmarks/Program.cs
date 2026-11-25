using System.Diagnostics;
using System.Globalization;
using DeliveryService.Core;

const int rows = 10_000;
const int iterations = 12;

var random = new Random(2026);
var statuses = Enum.GetValues<ShipmentStatus>();
var metrics = Enumerable.Range(0, rows).Select(index =>
{
    var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(index % 720);
    var status = statuses[index % statuses.Length];
    return new ShipmentMetrics(status, 100m + index % 5000, start,
        status == ShipmentStatus.Delivered ? start.AddHours(12 + index % 96) : null);
}).ToArray();

var shipments = metrics.Select((row, index) => new ShipmentInfo(index + 1,
    $"DS-2026-{index + 1:00000}", index % 40 + 1, $"Получатель {index % 40}", "+79990000000",
    $"ул. Примерная, {index % 100}", "Тестовая посылка", 1.5m, row.DeclaredAmount, null,
    "Курьер", row.Status, null, null, "", row.CreatedAt, row.CreatedAt, row.DeliveredAt, 1)).ToArray();

double naiveStats = Measure(() => NaiveStatistics(metrics), iterations);
double fastStats = Measure(() => ShipmentRules.StatisticsFromMetrics(metrics), iterations);
double naiveExport = Measure(() => NaiveCsv(shipments), 3);
double fastExport = Measure(() => Exchange.ExportCsv(shipments), 3);

Console.WriteLine($"rows={rows}");
Console.WriteLine($"iterations={iterations} (export: 3)");
Console.WriteLine($"statistics_before_ms={naiveStats:F2}");
Console.WriteLine($"statistics_after_ms={fastStats:F2}");
Console.WriteLine($"export_before_ms={naiveExport:F2}");
Console.WriteLine($"export_after_ms={fastExport:F2}");
Console.WriteLine($"statistics_speedup={naiveStats / fastStats:F2}x");
Console.WriteLine($"export_speedup={naiveExport / fastExport:F2}x");

static double Measure(Action action, int count)
{
    for (int warmup = 0; warmup < 2; warmup++)
        action();
    var samples = new List<double>();
    for (int index = 0; index < count; index++)
    {
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        samples.Add(watch.Elapsed.TotalMilliseconds);
    }
    samples.Sort();
    return samples[samples.Count / 2];
}

static DeliveryStatistics NaiveStatistics(IReadOnlyList<ShipmentMetrics> rows)
{
    var counts = new List<StatusCount>();
    foreach (var status in Enum.GetValues<ShipmentStatus>())
        counts.Add(new StatusCount(status, rows.Where(row => row.Status == status).Count()));
    decimal amount = rows.Sum(row => row.DeclaredAmount);
    decimal deliveredAmount = rows.Where(row => row.Status == ShipmentStatus.Delivered)
        .Sum(row => row.DeclaredAmount);
    var durations = rows.Where(row => row.DeliveredAt is not null)
        .Select(row => (row.DeliveredAt!.Value - row.CreatedAt).TotalDays).ToArray();
    return new DeliveryStatistics(rows.Count, rows.Count - counts.Single(row => row.Status == ShipmentStatus.Delivered).Count
        - counts.Single(row => row.Status == ShipmentStatus.Returned).Count,
        counts.Single(row => row.Status == ShipmentStatus.Delivered).Count,
        counts.Single(row => row.Status == ShipmentStatus.Returned).Count,
        amount, deliveredAmount, durations.Length == 0 ? null : durations.Average(),
        counts.ToArray());
}

static string NaiveCsv(IReadOnlyList<ShipmentInfo> rows)
{
    string csv = "\uFEFFNumber;Recipient;Amount;Status\n";
    foreach (var row in rows)
        csv += $"{row.TrackingNumber};{row.Recipient};" +
               $"{row.DeclaredAmount.ToString("0.00", CultureInfo.InvariantCulture)};" +
               $"{StatusNames.Russian(row.Status)}\n";
    return csv;
}
