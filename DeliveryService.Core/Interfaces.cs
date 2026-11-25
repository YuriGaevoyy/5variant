namespace DeliveryService.Core;

public interface IShipmentRepository
{
    Task<IReadOnlyList<ShipmentInfo>> FindShipments(UserInfo user, ShipmentStatus? status, string? search, CancellationToken ct);
    Task<ShipmentInfo?> FindShipment(int id, UserInfo user, CancellationToken ct);
    Task<IReadOnlyList<ShipmentMetrics>> FindMetrics(UserInfo user, CancellationToken ct);
    Task<int> Create(ShipmentInput input, UserInfo user, CancellationToken ct);
    Task<bool> Update(int id, ShipmentInput input, UserInfo user, CancellationToken ct);
    Task<bool> Transition(int id, UserInfo user, TransitionInput input, CancellationToken ct);
}

public interface IRecipientRepository
{
    Task<IReadOnlyList<RecipientInfo>> GetAll(CancellationToken ct);
    Task<int> Create(RecipientInput input, CancellationToken ct);
    Task<bool> Update(int id, RecipientInput input, CancellationToken ct);
    Task<ImportResult> Import(string csv, CancellationToken ct);
}

public interface IUserRepository
{
    Task<IReadOnlyList<UserInfo>> GetAll(bool couriersOnly, CancellationToken ct);
    Task<AppUser?> FindLogin(string login, CancellationToken ct);
}
