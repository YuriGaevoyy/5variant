using System.Diagnostics;

namespace DeliveryService.Core;

/// <summary>Жизненный цикл отправления службы доставки.</summary>
public enum ShipmentStatus
{
    Created,
    Accepted,
    InTransit,
    Delivered,
    Returned
}

public enum UserRole
{
    Admin,
    Operator,
    Courier
}

public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Operator = nameof(UserRole.Operator);
    public const string Courier = nameof(UserRole.Courier);
    public const string Staff = Admin + "," + Operator;
    public const string All = Admin + "," + Operator + "," + Courier;
}

public static class StatusNames
{
    public static string Russian(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created => "Создано",
        ShipmentStatus.Accepted => "Принято на склад",
        ShipmentStatus.InTransit => "В пути",
        ShipmentStatus.Delivered => "Доставлено",
        ShipmentStatus.Returned => "Возврат",
        _ => status.ToString()
    };
}

[DebuggerDisplay("Получатель #{Id} — {Name,nq}")]
public sealed class Recipient
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
}

[DebuggerDisplay("Пользователь #{Id} ({Role,nq})")]
public sealed class AppUser
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

[DebuggerDisplay("Отправление {TrackingNumber,nq} — {Status,nq} — версия {Version}")]
public sealed class Shipment
{
    public int Id { get; set; }
    public string TrackingNumber { get; set; } = "";
    public int RecipientId { get; set; }
    public Recipient Recipient { get; set; } = null!;
    public string Description { get; set; } = "";
    public string DeliveryAddress { get; set; } = "";
    public decimal WeightKg { get; set; }
    public decimal DeclaredAmount { get; set; }
    public int? CourierId { get; set; }
    public AppUser? Courier { get; set; }
    public string? Confirmation { get; set; }
    public ShipmentStatus Status { get; set; }
    public string? ReturnReason { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int Version { get; set; }
}

public sealed record RecipientInput(string Name, string Phone, string Address, string? Email);
public sealed record RecipientInfo(int Id, string Name, string Phone, string Address, string Email);
public sealed record UserInfo(int Id, string Login, string DisplayName, string Role);
public sealed record LoginInput(string Login, string Password);
public sealed record LoginResult(string Token, UserInfo User);
public sealed record ShipmentInput(int RecipientId, string Description, string DeliveryAddress,
    decimal WeightKg, decimal DeclaredAmount, int? CourierId, string? Notes, int? Version = null);
public sealed record TransitionInput(ShipmentStatus Status, string? Reason, string? Confirmation, int Version);
public sealed record ShipmentInfo(int Id, string TrackingNumber, int RecipientId, string Recipient,
    string RecipientPhone, string DeliveryAddress, string Description, decimal WeightKg,
    decimal DeclaredAmount, int? CourierId, string? Courier, ShipmentStatus Status,
    string? Confirmation, string? ReturnReason, string Notes, DateTime CreatedAt,
    DateTime? AcceptedAt, DateTime? DeliveredAt, int Version);
public sealed record StatusCount(ShipmentStatus Status, int Count);
public readonly record struct ShipmentMetrics(ShipmentStatus Status, decimal DeclaredAmount,
    DateTime CreatedAt, DateTime? DeliveredAt);
public sealed record DeliveryStatistics(int Total, int Active, int Delivered, int Returned,
    decimal TotalAmount, decimal DeliveredAmount, double? AverageDays, IReadOnlyList<StatusCount> ByStatus);
public sealed record ImportResult(int Imported, IReadOnlyList<CsvRowError> Errors);
public sealed record HealthInfo(string Status, int Shipments);
public sealed record CsvRowError(long Line, string Message);

public class RuleException(string message) : Exception(message);
public sealed class ShipmentConflictException() : Exception("Отправление изменилось. Обновите список и повторите действие.");
public sealed class CsvImportException(IReadOnlyList<CsvRowError> errors) :
    RuleException(string.Join(Environment.NewLine,
        errors.Take(10).Select(row => $"Строка {row.Line}: {row.Message}")) +
        (errors.Count > 10 ? $"{Environment.NewLine}Всего ошибок: {errors.Count}." : ""))
{
    public IReadOnlyList<CsvRowError> Errors { get; } = errors;
}
