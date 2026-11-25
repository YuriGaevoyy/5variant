using DeliveryService.Core;
using Microsoft.EntityFrameworkCore;

namespace DeliveryService.Data;

/// <summary>Демоданные: четыре учётных записи, получатели и девять отправлений на всех этапах.</summary>
public static class DeliveryDemo
{
    public static async Task Seed(DeliveryDb db, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct) || await db.Shipments.AnyAsync(ct))
            return;

        var hasher = new Pbkdf2PasswordHasher();
        db.Users.AddRange(
            User(1, "admin", "Администратор", Roles.Admin, "Admin@2026!", hasher),
            User(2, "operator", "Оператор", Roles.Operator, "Operator@2026!", hasher),
            User(3, "courier1", "Курьер Антон", Roles.Courier, "Courier1@2026!", hasher),
            User(4, "courier2", "Курьер Мария", Roles.Courier, "Courier2@2026!", hasher));

        string[] names = ["ООО «Лазурь»", "Ирина Соколова", "МАОУ Школа № 12", "Павел Ким",
            "ООО «Тёплый дом»", "Наталья Воронова", "Артём Белов", "ООО «Мегаполис»"];
        string[] phones = ["+7 495 120-11-01", "+7 916 220-13-45", "+7 495 778-00-12", "+7 903 114-67-89",
            "+7 495 333-44-55", "+7 925 700-18-22", "+7 999 321-45-67", "+7 495 900-02-03"];
        string[] addresses = ["ул. Тверская, 12", "пр-т Мира, 44, кв. 8", "ул. Школьная, 3",
            "Набережная, 17, кв. 41", "ул. Садовая, 9", "ул. Парковая, 22, кв. 5",
            "ул. Новая, 7", "Бизнес-центр, офис 310"];
        var now = DateTime.UtcNow;
        var recipients = new List<Recipient>();
        for (int index = 0; index < names.Length; index++)
        {
            recipients.Add(new Recipient
            {
                Name = names[index], Phone = phones[index], Address = addresses[index],
                Email = $"recipient{index + 1}@example.test", RegisteredAt = now.AddDays(-60 + index)
            });
        }
        db.Recipients.AddRange(recipients);
        await db.SaveChangesAsync(ct);

        int[] recipientIndexes = [0, 1, 2, 3, 4, 5, 6, 7, 1];
        string[] descriptions = ["Документы и договор", "Посылка 2,4 кг", "Учебные материалы",
            "Электроника", "Комплект постельного белья", "Подарочный набор", "Запчасти", "Офисная техника", "Книги"];
        ShipmentStatus[] statuses = [ShipmentStatus.Created, ShipmentStatus.Created, ShipmentStatus.Accepted,
            ShipmentStatus.Accepted, ShipmentStatus.InTransit, ShipmentStatus.InTransit,
            ShipmentStatus.Delivered, ShipmentStatus.Delivered, ShipmentStatus.Returned];
        int?[] couriers = [null, null, null, null, 3, 4, 3, 4, 3];
        int[] ages = [0, 1, 2, 3, 4, 2, 9, 12, 6];

        for (int index = 0; index < statuses.Length; index++)
        {
            var created = now.AddDays(-ages[index]).AddHours(-index);
            var shipment = new Shipment
            {
                TrackingNumber = $"DS-2026-{index + 1:0000}",
                RecipientId = recipients[recipientIndexes[index]].Id,
                Description = descriptions[index],
                DeliveryAddress = recipients[recipientIndexes[index]].Address,
                WeightKg = 0.5m + index * 0.4m,
                DeclaredAmount = 900m + index * 325m,
                CourierId = couriers[index],
                Status = statuses[index],
                Notes = index % 3 == 0 ? "Доставить в рабочие часы" : "",
                CreatedAt = created,
                Version = 1
            };
            if (statuses[index] is ShipmentStatus.Accepted or ShipmentStatus.InTransit or
                ShipmentStatus.Delivered or ShipmentStatus.Returned)
                shipment.AcceptedAt = created.AddHours(3);
            if (statuses[index] == ShipmentStatus.Delivered)
            {
                shipment.DeliveredAt = created.AddDays(2);
                shipment.Confirmation = recipients[recipientIndexes[index]].Name;
            }
            if (statuses[index] == ShipmentStatus.Returned)
                shipment.ReturnReason = "Получатель отказался от отправления";
            db.Shipments.Add(shipment);
        }

        await db.SaveChangesAsync(ct);
    }

    private static AppUser User(int id, string login, string displayName, string role, string password,
        IPasswordHasher hasher) => new()
    {
        Id = id, Login = login, DisplayName = displayName, Role = role, PasswordHash = hasher.Hash(password)
    };
}
