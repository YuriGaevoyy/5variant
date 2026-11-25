using System.Diagnostics;

namespace DeliveryService.Core;

/// <summary>Серверные правила жизненного цикла и статистики отправлений.</summary>
public static class ShipmentRules
{
    public static bool IsFinal(ShipmentStatus status) =>
        status is ShipmentStatus.Delivered or ShipmentStatus.Returned;

    public static IReadOnlyList<ShipmentStatus> Next(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created => [ShipmentStatus.Accepted, ShipmentStatus.Returned],
        ShipmentStatus.Accepted => [ShipmentStatus.InTransit, ShipmentStatus.Returned],
        ShipmentStatus.InTransit => [ShipmentStatus.Delivered, ShipmentStatus.Returned],
        ShipmentStatus.Delivered => [ShipmentStatus.Returned],
        _ => []
    };

    public static void Transition(Shipment shipment, ShipmentStatus target, string? reason,
        string? confirmation, DateTime now)
    {
        if (!Next(shipment.Status).Contains(target))
            throw new RuleException("Переход из текущего статуса недоступен.");

        if (target == ShipmentStatus.Accepted)
        {
            if (shipment.Recipient is null || string.IsNullOrWhiteSpace(shipment.DeliveryAddress))
                throw new RuleException("Для приёмки нужны получатель и адрес доставки.");
            shipment.AcceptedAt = now;
        }

        // Бизнес-правило 1: в путь можно отправить только с назначенным курьером.
        if (target == ShipmentStatus.InTransit && shipment.CourierId is null)
            throw new RuleException("Перед передачей в доставку назначьте курьера.");

        // Бизнес-правило 2: доставка подтверждается суммой и данными о вручении.
        if (target == ShipmentStatus.Delivered)
        {
            if (shipment.DeclaredAmount <= 0)
                throw new RuleException("Для доставки укажите положительную объявленную сумму.");
            shipment.Confirmation = InputRules.Required(confirmation, "Подтверждение получения", 150);
            shipment.DeliveredAt = now;
        }

        // Бизнес-правило 3: возврат — конечный статус с обязательной причиной.
        if (target == ShipmentStatus.Returned)
        {
            string returnReason = InputRules.Required(reason, "Причина возврата", 300);
            if (returnReason.Length < 5)
                throw new RuleException("Причина возврата должна содержать не менее 5 символов.");
            shipment.ReturnReason = returnReason;
        }

        shipment.Status = target;
        shipment.Version++;
        Debug.Assert(shipment.Status != ShipmentStatus.Delivered || shipment.DeliveredAt.HasValue);
        Debug.Assert(shipment.Status != ShipmentStatus.Returned || !string.IsNullOrWhiteSpace(shipment.ReturnReason));
    }

    public static DeliveryStatistics Statistics(IReadOnlyList<ShipmentInfo> rows) =>
        StatisticsFromMetrics(rows.Select(row => new ShipmentMetrics(row.Status, row.DeclaredAmount,
            row.CreatedAt, row.DeliveredAt)));

    public static DeliveryStatistics StatisticsFromMetrics(IEnumerable<ShipmentMetrics> rows)
    {
        var statuses = Enum.GetValues<ShipmentStatus>();
        var counts = new int[statuses.Length];
        int total = 0, durationCount = 0;
        decimal totalAmount = 0, deliveredAmount = 0;
        double duration = 0;

        foreach (var row in rows)
        {
            total++;
            if ((uint)row.Status < counts.Length)
                counts[(int)row.Status]++;
            totalAmount += row.DeclaredAmount;
            if (row.Status != ShipmentStatus.Delivered)
                continue;
            deliveredAmount += row.DeclaredAmount;
            if (row.DeliveredAt is { } delivered && delivered >= row.CreatedAt)
            {
                duration += (delivered - row.CreatedAt).TotalDays;
                durationCount++;
            }
        }

        int deliveredCount = counts[(int)ShipmentStatus.Delivered], returned = counts[(int)ShipmentStatus.Returned];
        return new DeliveryStatistics(total, total - deliveredCount - returned, deliveredCount, returned,
            totalAmount, deliveredAmount,
            durationCount == 0 ? null : Math.Round(duration / durationCount, 2),
            statuses.Select(status => new StatusCount(status, counts[(int)status])).ToArray());
    }
}
