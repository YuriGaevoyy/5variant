using System.Linq.Expressions;
using DeliveryService.Core;
using Microsoft.EntityFrameworkCore;

namespace DeliveryService.Data;

public sealed class DeliveryStore(DeliveryDb db, TimeProvider time) : IShipmentRepository, IRecipientRepository, IUserRepository
{
    private static readonly Expression<Func<Shipment, ShipmentInfo>> Projection = row =>
        new ShipmentInfo(row.Id, row.TrackingNumber, row.RecipientId, row.Recipient.Name, row.Recipient.Phone,
            row.DeliveryAddress, row.Description, row.WeightKg, row.DeclaredAmount, row.CourierId,
            row.Courier == null ? null : row.Courier.DisplayName, row.Status, row.Confirmation,
            row.ReturnReason, row.Notes, row.CreatedAt, row.AcceptedAt, row.DeliveredAt, row.Version);

    private IQueryable<Shipment> Visible(UserInfo user) =>
        user.Role == Roles.Courier ? db.Shipments.Where(row => row.CourierId == user.Id) : db.Shipments;

    public async Task<IReadOnlyList<ShipmentInfo>> FindShipments(UserInfo user, ShipmentStatus? status,
        string? search, CancellationToken ct)
    {
        var query = Visible(user).AsNoTracking();
        if (status.HasValue)
            query = query.Where(row => row.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(row => DeliveryDatabase.DeliveryContains(row.TrackingNumber, term) ||
                DeliveryDatabase.DeliveryContains(row.Recipient.Name, term) ||
                DeliveryDatabase.DeliveryContains(row.DeliveryAddress, term) ||
                DeliveryDatabase.DeliveryContains(row.Description, term) ||
                row.Recipient.Phone.Contains(term) ||
                (row.Courier != null && DeliveryDatabase.DeliveryContains(row.Courier.DisplayName, term)));
        }
        return await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Select(Projection).ToListAsync(ct);
    }

    public Task<ShipmentInfo?> FindShipment(int id, UserInfo user, CancellationToken ct) =>
        Visible(user).AsNoTracking().Where(row => row.Id == id).Select(Projection).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ShipmentMetrics>> FindMetrics(UserInfo user, CancellationToken ct) =>
        await Visible(user).AsNoTracking()
            .Select(row => new ShipmentMetrics(row.Status, row.DeclaredAmount, row.CreatedAt, row.DeliveredAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RecipientInfo>> GetAll(CancellationToken ct) =>
        await db.Recipients.AsNoTracking()
            .OrderBy(row => row.Name).ThenBy(row => row.Id)
            .Select(row => new RecipientInfo(row.Id, row.Name, row.Phone, row.Address, row.Email))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserInfo>> GetAll(bool couriersOnly, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(row => !couriersOnly || row.Role == Roles.Courier)
            .OrderBy(row => row.Id)
            .Select(row => new UserInfo(row.Id, row.Login, row.DisplayName, row.Role))
            .ToListAsync(ct);

    public Task<AppUser?> FindLogin(string login, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Login == login, ct);

    public async Task<int> Create(RecipientInput input, CancellationToken ct)
    {
        var values = InputRules.Recipient(input);
        var recipient = new Recipient
        {
            Name = values.Name, Phone = values.Phone, Address = values.Address, Email = values.Email ?? "",
            RegisteredAt = time.GetUtcNow().UtcDateTime
        };
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync(ct);
        return recipient.Id;
    }

    public async Task<bool> Update(int id, RecipientInput input, CancellationToken ct)
    {
        var recipient = await db.Recipients.FindAsync([id], ct);
        if (recipient is null)
            return false;
        var values = InputRules.Recipient(input);
        recipient.Name = values.Name;
        recipient.Phone = values.Phone;
        recipient.Address = values.Address;
        recipient.Email = values.Email ?? "";
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ImportResult> Import(string csv, CancellationToken ct)
    {
        var rows = Exchange.ReadRecipients(csv);
        var recipients = rows.Select(row => new Recipient
        {
            Name = row.Name, Phone = row.Phone, Address = row.Address, Email = row.Email ?? "",
            RegisteredAt = time.GetUtcNow().UtcDateTime
        });
        db.Recipients.AddRange(recipients);
        await db.SaveChangesAsync(ct);
        return new ImportResult(rows.Count, []);
    }

    public async Task<int> Create(ShipmentInput input, UserInfo user, CancellationToken ct)
    {
        var shipment = new Shipment { CreatedAt = time.GetUtcNow().UtcDateTime, Version = 1 };
        await Apply(shipment, input, ct);
        shipment.TrackingNumber = await NextNumber(ct);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(ct);
        return shipment.Id;
    }

    public async Task<bool> Update(int id, ShipmentInput input, UserInfo user, CancellationToken ct)
    {
        var shipment = await Visible(user).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (shipment is null)
            return false;
        InputRules.CheckVersion(shipment, input.Version);
        if (ShipmentRules.IsFinal(shipment.Status))
            throw new RuleException("Завершённое отправление закрыто для редактирования.");
        await Apply(shipment, input, ct);
        shipment.Version++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> Transition(int id, UserInfo user, TransitionInput input, CancellationToken ct)
    {
        var shipment = await Visible(user)
            .Include(row => row.Recipient)
            .SingleOrDefaultAsync(row => row.Id == id, ct);
        if (shipment is null)
            return false;
        InputRules.CheckVersion(shipment, input.Version);
        ShipmentRules.Transition(shipment, input.Status, input.Reason, input.Confirmation,
            time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> NextNumber(CancellationToken ct)
    {
        int next = await db.Shipments.CountAsync(ct) + 1;
        return $"DS-{DateTime.UtcNow.Year}-{next:0000}";
    }

    private async Task Apply(Shipment shipment, ShipmentInput input, CancellationToken ct)
    {
        shipment.Description = InputRules.Required(input.Description, "Описание", 180);
        shipment.DeliveryAddress = InputRules.Required(input.DeliveryAddress, "Адрес доставки", 220);
        shipment.Notes = InputRules.Optional(input.Notes, "Примечания", 2000);
        InputRules.CheckAmount(input.DeclaredAmount);
        InputRules.CheckWeight(input.WeightKg);
        shipment.WeightKg = input.WeightKg;

        if (!await db.Recipients.AnyAsync(row => row.Id == input.RecipientId, ct))
            throw new RuleException("Выберите существующего получателя.");
        if (input.CourierId is { } courierId &&
            !await db.Users.AnyAsync(row => row.Id == courierId && row.Role == Roles.Courier, ct))
            throw new RuleException("Курьером можно назначить только пользователя с ролью «Курьер».");
        if (shipment.Status is ShipmentStatus.InTransit && input.CourierId is null)
            throw new RuleException("У отправления в пути должен быть курьер.");

        shipment.RecipientId = input.RecipientId;
        shipment.CourierId = input.CourierId;
        shipment.DeclaredAmount = input.DeclaredAmount;
        shipment.WeightKg = input.WeightKg;
    }
}
