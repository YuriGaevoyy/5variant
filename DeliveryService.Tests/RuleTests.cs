using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DeliveryService.Core;
using Xunit;

namespace DeliveryService.Tests;

public sealed class RuleTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Shipment New(ShipmentStatus status = ShipmentStatus.Created, decimal amount = 1500m,
        int? courierId = 3) => new()
    {
        Id = 1,
        TrackingNumber = "DS-2026-0001",
        RecipientId = 1,
        Recipient = new Recipient { Id = 1, Name = "Ирина" },
        Description = "Документы",
        DeliveryAddress = "ул. Тверская, 12",
        DeclaredAmount = amount,
        CourierId = courierId,
        Status = status,
        CreatedAt = Start,
        Version = 1
    };

    [Theory]
    [InlineData(ShipmentStatus.Created, 2)]
    [InlineData(ShipmentStatus.Accepted, 2)]
    [InlineData(ShipmentStatus.InTransit, 2)]
    [InlineData(ShipmentStatus.Delivered, 1)]
    [InlineData(ShipmentStatus.Returned, 0)]
    public void Next_returns_allowed_transitions(ShipmentStatus status, int count) =>
        Assert.Equal(count, ShipmentRules.Next(status).Count);

    [Theory]
    [InlineData(ShipmentStatus.Created, false)]
    [InlineData(ShipmentStatus.Accepted, false)]
    [InlineData(ShipmentStatus.InTransit, false)]
    [InlineData(ShipmentStatus.Delivered, true)]
    [InlineData(ShipmentStatus.Returned, true)]
    public void IsFinal_marks_only_terminal_statuses(ShipmentStatus status, bool expected) =>
        Assert.Equal(expected, ShipmentRules.IsFinal(status));

    [Theory]
    [InlineData(ShipmentStatus.Created, "Создано")]
    [InlineData(ShipmentStatus.Accepted, "Принято на склад")]
    [InlineData(ShipmentStatus.InTransit, "В пути")]
    [InlineData(ShipmentStatus.Delivered, "Доставлено")]
    [InlineData(ShipmentStatus.Returned, "Возврат")]
    public void StatusNames_returns_text_labels(ShipmentStatus status, string label) =>
        Assert.Equal(label, StatusNames.Russian(status));

    [Fact]
    public void Accept_sets_timestamp_and_increments_version()
    {
        var shipment = New();
        ShipmentRules.Transition(shipment, ShipmentStatus.Accepted, null, null, Start.AddHours(2));
        Assert.Equal(Start.AddHours(2), shipment.AcceptedAt);
        Assert.Equal(2, shipment.Version);
    }

    [Fact]
    public void Created_cannot_jump_to_in_transit() =>
        Assert.Throws<RuleException>(() => ShipmentRules.Transition(New(), ShipmentStatus.InTransit, null, null, Start));

    [Fact]
    public void In_transit_requires_courier()
    {
        var shipment = New(ShipmentStatus.Accepted, courierId: null);
        var error = Assert.Throws<RuleException>(() =>
            ShipmentRules.Transition(shipment, ShipmentStatus.InTransit, null, null, Start));
        Assert.Contains("курьера", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delivery_requires_positive_amount()
    {
        var shipment = New(ShipmentStatus.InTransit, amount: 0);
        Assert.Throws<RuleException>(() => ShipmentRules.Transition(shipment, ShipmentStatus.Delivered,
            null, "Ирина", Start));
    }

    [Fact]
    public void Delivery_requires_confirmation()
    {
        var shipment = New(ShipmentStatus.InTransit);
        Assert.Throws<RuleException>(() => ShipmentRules.Transition(shipment, ShipmentStatus.Delivered,
            null, "  ", Start));
    }

    [Fact]
    public void Delivery_with_amount_and_confirmation_succeeds()
    {
        var shipment = New(ShipmentStatus.InTransit);
        ShipmentRules.Transition(shipment, ShipmentStatus.Delivered, null, "Ирина Соколова", Start.AddDays(2));
        Assert.Equal("Ирина Соколова", shipment.Confirmation);
        Assert.Equal(Start.AddDays(2), shipment.DeliveredAt);
    }

    [Theory]
    [InlineData(ShipmentStatus.Created)]
    [InlineData(ShipmentStatus.Accepted)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivered)]
    public void Return_is_allowed_from_every_active_stage(ShipmentStatus from)
    {
        var shipment = New(from);
        ShipmentRules.Transition(shipment, ShipmentStatus.Returned, "Получатель отказался", null, Start.AddDays(1));
        Assert.Equal(ShipmentStatus.Returned, shipment.Status);
        Assert.Equal("Получатель отказался", shipment.ReturnReason);
    }

    [Fact]
    public void Return_requires_reason_of_at_least_five_characters()
    {
        var shipment = New();
        Assert.Throws<RuleException>(() =>
            ShipmentRules.Transition(shipment, ShipmentStatus.Returned, "нет", null, Start));
    }

    [Fact]
    public void Return_is_terminal()
    {
        var shipment = New();
        ShipmentRules.Transition(shipment, ShipmentStatus.Returned, "Отказ получателя", null, Start);
        Assert.Empty(ShipmentRules.Next(shipment.Status));
        Assert.Throws<RuleException>(() =>
            ShipmentRules.Transition(shipment, ShipmentStatus.Accepted, null, null, Start));
    }

    [Fact]
    public void Statistics_calculates_counts_amounts_and_average_days()
    {
        var rows = new[]
        {
            new ShipmentInfo(1, "DS-1", 1, "А", "", "", "", 1, 100m, null, null, ShipmentStatus.Created,
                null, null, "", Start, null, null, 1),
            new ShipmentInfo(2, "DS-2", 1, "Б", "", "", "", 1, 200m, null, null, ShipmentStatus.Delivered,
                "Б", null, "", Start, Start, Start.AddDays(4), 1),
            new ShipmentInfo(3, "DS-3", 1, "В", "", "", "", 1, 300m, null, null, ShipmentStatus.Delivered,
                "В", null, "", Start, Start, Start.AddDays(6), 1),
            new ShipmentInfo(4, "DS-4", 1, "Г", "", "", "", 1, 50m, null, null, ShipmentStatus.Returned,
                null, "Отказ", "", Start, null, null, 1)
        };
        var stats = ShipmentRules.Statistics(rows);
        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.Delivered);
        Assert.Equal(1, stats.Returned);
        Assert.Equal(1, stats.Active);
        Assert.Equal(650m, stats.TotalAmount);
        Assert.Equal(500m, stats.DeliveredAmount);
        Assert.Equal(5.0, stats.AverageDays);
        Assert.Equal(5, stats.ByStatus.Count);
    }

    [Fact]
    public void Statistics_handles_empty_input()
    {
        var stats = ShipmentRules.Statistics([]);
        Assert.Equal(0, stats.Total);
        Assert.Null(stats.AverageDays);
        Assert.Equal(0m, stats.TotalAmount);
    }

    [Theory]
    [InlineData("1500,50", 1500.50)]
    [InlineData("999", 999)]
    [InlineData("", 0)]
    [InlineData("75.25", 75.25)]
    public void ParseAmount_accepts_valid_values(string text, double expected) =>
        Assert.Equal((decimal)expected, InputRules.ParseAmount(text));

    [Theory]
    [InlineData("-1")]
    [InlineData("1,234")]
    [InlineData("1 000")]
    [InlineData("abc")]
    public void ParseAmount_rejects_invalid_values(string text) =>
        Assert.Throws<RuleException>(() => InputRules.ParseAmount(text));

    [Theory]
    [InlineData("2,5", 2.5)]
    [InlineData("0", 0)]
    [InlineData("1000", 1000)]
    public void ParseWeight_accepts_valid_values(string text, double expected) =>
        Assert.Equal((decimal)expected, InputRules.ParseWeight(text));

    [Fact]
    public void ParseWeight_rejects_negative() => Assert.Throws<RuleException>(() => InputRules.ParseWeight("-2"));

    [Fact]
    public void Required_rejects_whitespace() =>
        Assert.Throws<RuleException>(() => InputRules.Required("   ", "Получатель", 100));

    [Fact]
    public void Optional_rejects_long_values() =>
        Assert.Throws<RuleException>(() => InputRules.Optional(new string('a', 201), "Примечания", 200));

    [Fact]
    public void Recipient_validation_rejects_bad_email() =>
        Assert.Throws<RuleException>(() => InputRules.Recipient(
            new RecipientInput("Ирина", "+79990000000", "ул. Тверская", "not-an-email")));

    [Fact]
    public void CheckVersion_detects_stale_edit()
    {
        var shipment = New();
        Assert.Throws<ShipmentConflictException>(() => InputRules.CheckVersion(shipment, 0));
        Assert.Throws<RuleException>(() => InputRules.CheckVersion(shipment, null));
        InputRules.CheckVersion(shipment, 1);
    }

    [Fact]
    public void Csv_import_reads_semicolon_and_quotes()
    {
        const string csv = "Name,Phone,Address,Email\n\"ООО «Лазурь»\",+74951234567,\"ул. Тверская, 12\",a@example.test";
        var rows = Exchange.ReadRecipients(csv);
        Assert.Single(rows);
        Assert.Equal("ООО «Лазурь»", rows[0].Name);
        Assert.Equal("ул. Тверская, 12", rows[0].Address);
    }

    [Fact]
    public void Csv_import_reports_problem_line_numbers()
    {
        const string csv = "Name,Phone,Address,Email\nИрина,,ул. Мира,test@example.test\n,123,ул. Новая,test@example.test";
        var error = Assert.Throws<CsvImportException>(() => Exchange.ReadRecipients(csv));
        Assert.Contains(error.Errors, row => row.Line == 2);
        Assert.Contains(error.Errors, row => row.Line == 3);
    }

    [Fact]
    public void Csv_export_contains_bom_header_and_russian_status()
    {
        var rows = new[] { ShipmentInfoForExport() };
        string csv = Exchange.ExportCsv(rows);
        Assert.StartsWith("\uFEFF", csv, StringComparison.Ordinal);
        Assert.Contains("Number;Recipient", csv, StringComparison.Ordinal);
        Assert.Contains("В пути", csv, StringComparison.Ordinal);
        Assert.Contains("2500.00", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_export_uses_string_enums()
    {
        string json = Exchange.ExportJson([ShipmentInfoForExport()]);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("InTransit", document.RootElement[0].GetProperty("status").GetString());
    }

    [Fact]
    public void Xml_export_contains_all_key_fields()
    {
        var document = XDocument.Parse(Exchange.ExportXml([ShipmentInfoForExport()]));
        Assert.Equal("DS-2026-0042", document.Root!.Elements("shipment").Single().Attribute("number")?.Value);
        Assert.Equal("В пути", document.Root.Descendants("status").Single().Value);
    }

    [Fact]
    public void Password_hasher_verifies_and_rejects_wrong_password()
    {
        var hasher = new Pbkdf2PasswordHasher();
        string hash = hasher.Hash("Admin@2026!");
        Assert.True(hasher.Verify("Admin@2026!", hash));
        Assert.False(hasher.Verify("wrong", hash));
        Assert.False(hasher.Verify("Admin@2026!", "bad-hash"));
    }

    private static ShipmentInfo ShipmentInfoForExport() =>
        new(42, "DS-2026-0042", 1, "Ирина", "+79990000000", "ул. Мира, 4", "Посылка", 2.5m, 2500m, 3,
            "Антон", ShipmentStatus.InTransit, null, null, "", Start, Start.AddHours(2), null, 3);
}
