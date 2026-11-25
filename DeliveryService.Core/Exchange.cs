using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace DeliveryService.Core;

/// <summary>Источники, приёмники и сопоставление полей обмена данными.</summary>
public static class Exchange
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public static IReadOnlyList<RecipientInput> ReadRecipients(string csv)
    {
        var errors = new List<CsvRowError>();
        var values = new List<RecipientInput>();
        long lineNumber = 0;
        foreach (var rawLine in csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            string line = rawLine.TrimStart('\uFEFF');
            if (lineNumber == 1 && line.StartsWith("Name", StringComparison.OrdinalIgnoreCase))
                continue;
            var cells = Split(line);
            if (cells.Count is < 3 or > 4)
            {
                errors.Add(new CsvRowError(lineNumber, "Ожидается 3 или 4 поля: Name,Phone,Address,Email."));
                continue;
            }

            try
            {
                values.Add(InputRules.Recipient(new RecipientInput(cells[0], cells[1], cells[2],
                    cells.Count == 4 ? cells[3] : null)));
            }
            catch (RuleException error)
            {
                errors.Add(new CsvRowError(lineNumber, error.Message));
            }
        }

        if (values.Count == 0 && errors.Count == 0)
            errors.Add(new CsvRowError(1, "Файл не содержит получателей."));
        if (errors.Count > 0)
            throw new CsvImportException(errors);
        return values;
    }

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char symbol = line[index];
            if (symbol == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                    quoted = !quoted;
            }
            else if ((symbol is ';' or ',') && !quoted)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
                current.Append(symbol);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    public static string ExportCsv(IReadOnlyList<ShipmentInfo> shipments)
    {
        var result = new StringBuilder();
        result.Append('\uFEFF');
        result.AppendLine("Number;Recipient;Phone;Address;Description;WeightKg;Amount;Courier;Status;CreatedAt;DeliveredAt;Confirmation;ReturnReason");
        foreach (var row in shipments)
            result.AppendLine(string.Join(';',
                Cell(row.TrackingNumber), Cell(row.Recipient), Cell(row.RecipientPhone), Cell(row.DeliveryAddress),
                Cell(row.Description), row.WeightKg.ToString("0.00", CultureInfo.InvariantCulture),
                row.DeclaredAmount.ToString("0.00", CultureInfo.InvariantCulture), Cell(row.Courier ?? ""),
                StatusNames.Russian(row.Status), row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                row.DeliveredAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                Cell(row.Confirmation ?? ""), Cell(row.ReturnReason ?? "")));
        return result.ToString();
    }

    private static string Cell(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    public static string ExportJson(IReadOnlyList<ShipmentInfo> shipments) =>
        JsonSerializer.Serialize(shipments, JsonOptions);

    public static string ExportXml(IReadOnlyList<ShipmentInfo> shipments)
    {
        var root = new XElement("shipments");
        foreach (var row in shipments)
            root.Add(new XElement("shipment",
                new XAttribute("number", row.TrackingNumber),
                new XElement("recipient", row.Recipient),
                new XElement("phone", row.RecipientPhone),
                new XElement("address", row.DeliveryAddress),
                new XElement("description", row.Description),
                new XElement("weightKg", row.WeightKg.ToString("0.00", CultureInfo.InvariantCulture)),
                new XElement("amount", row.DeclaredAmount.ToString("0.00", CultureInfo.InvariantCulture)),
                new XElement("courier", row.Courier ?? ""),
                new XElement("status", StatusNames.Russian(row.Status)),
                new XElement("createdAt", row.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new XElement("deliveredAt", row.DeliveredAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) ?? ""),
                new XElement("confirmation", row.Confirmation ?? ""),
                new XElement("returnReason", row.ReturnReason ?? "")));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
}
