using System.Globalization;
using System.Windows.Input;
using DeliveryService.Core;

namespace DeliveryService.Desktop;

internal sealed class WorkspaceViewModel : AsyncViewModel
{
    private readonly DeliveryClient? api;
    private readonly LoginResult? login;
    private readonly IDialogService? dialogs;
    private readonly Action? onLogout;
    private int tab;
    private string search = "";
    private StatusOption? selectedFilter;
    private ShipmentInfo? selectedShipment;
    private RecipientInfo? selectedRecipient;
    private IReadOnlyList<ShipmentInfo> shipments = [];
    private IReadOnlyList<RecipientInfo> recipients = [];
    private IReadOnlyList<UserInfo> couriers = [];
    private IReadOnlyList<UserInfo> users = [];
    private DeliveryStatistics statistics = new(0, 0, 0, 0, 0, 0, null, []);
    private IReadOnlyList<StatusRow> statusRows = [];

    public WorkspaceViewModel()
    {
        SetTab = new Command(_ => { });
        RefreshShipments = new Command(_ => { });
        NewShipment = new Command(_ => { });
        EditShipment = new Command(_ => { });
        DetailsShipment = new Command(_ => { });
        Transition = new Command(_ => { });
        Export = new Command(_ => { });
        RefreshRecipients = new Command(_ => { });
        NewRecipient = new Command(_ => { });
        EditRecipient = new Command(_ => { });
        Import = new Command(_ => { });
        RefreshStatistics = new Command(_ => { });
        RefreshUsers = new Command(_ => { });
        Logout = new Command(_ => { });
    }

    public WorkspaceViewModel(DeliveryClient api, LoginResult login, IDialogService dialogs, Action onLogout)
    {
        this.api = api;
        this.login = login;
        this.dialogs = dialogs;
        this.onLogout = onLogout;

        SetTab = new Command(parameter => Tab = System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture));
        RefreshShipments = Run(async _ =>
        {
            Shipments = await api.Shipments(SelectedFilter?.Value, Search);
            Changed(nameof(ShipmentCount));
            if (selectedShipment is not null)
                SelectedShipment = shipments.FirstOrDefault(row => row.Id == selectedShipment.Id);
        });
        NewShipment = Run(async _ =>
        {
            var input = dialogs.EditShipment(null, recipients, couriers);
            if (input is null)
                return;
            await api.SaveShipment(null, input);
            Message = "Отправление создано.";
            await RefreshList();
        }, () => IsStaff);
        EditShipment = Run(async _ =>
        {
            if (selectedShipment is null)
                throw new RuleException("Выберите отправление.");
            var input = dialogs.EditShipment(selectedShipment, recipients, couriers);
            if (input is null)
                return;
            await api.SaveShipment(selectedShipment.Id, input with { Version = selectedShipment.Version });
            Message = "Отправление обновлено.";
            await RefreshList();
        }, () => IsStaff && selectedShipment is not null);
        DetailsShipment = Run(_ =>
        {
            if (selectedShipment is null)
                throw new RuleException("Выберите отправление.");
            dialogs.ShowDetails(selectedShipment);
            return Task.CompletedTask;
        }, () => selectedShipment is not null);
        Transition = Run(async _ =>
        {
            if (selectedShipment is null)
                throw new RuleException("Выберите отправление.");
            var input = dialogs.ChooseTransition(selectedShipment, IsStaff);
            if (input is null)
                return;
            await api.Transition(selectedShipment.Id, input with { Version = selectedShipment.Version });
            Message = "Этап отправления обновлён.";
            await RefreshList();
            await RefreshStats();
        }, () => selectedShipment is not null && !ShipmentRules.IsFinal(selectedShipment.Status));
        Export = Run(async parameter =>
        {
            string format = parameter?.ToString() ?? "csv";
            byte[] bytes = await api.Export(format, SelectedFilter?.Value, Search);
            dialogs.SaveExport(format, bytes);
            Message = $"Выгрузка {format.ToUpperInvariant()} сохранена.";
        }, () => IsStaff);
        RefreshRecipients = Run(async _ =>
        {
            Recipients = await api.Recipients();
            Couriers = await api.Couriers();
        }, () => IsStaff);
        NewRecipient = Run(async _ =>
        {
            var input = dialogs.EditRecipient(null);
            if (input is null)
                return;
            await api.SaveRecipient(null, input);
            Message = "Получатель добавлен.";
            await RefreshRecipientsList();
        }, () => IsStaff);
        EditRecipient = Run(async _ =>
        {
            if (selectedRecipient is null)
                throw new RuleException("Выберите получателя.");
            var input = dialogs.EditRecipient(selectedRecipient);
            if (input is null)
                return;
            await api.SaveRecipient(selectedRecipient.Id, input);
            Message = "Получатель обновлён.";
            await RefreshRecipientsList();
        }, () => IsStaff && selectedRecipient is not null);
        Import = Run(async _ =>
        {
            string? csv = dialogs.PickCsv();
            if (csv is null)
                return;
            var result = await api.Import(csv);
            Message = result.Errors.Count == 0
                ? $"Импортировано получателей: {result.Imported}."
                : $"Импортировано: {result.Imported}. Ошибки: {string.Join("; ", result.Errors.Take(5))}";
            await RefreshRecipientsList();
        }, () => IsStaff);
        RefreshStatistics = Run(async _ => await RefreshStats());
        RefreshUsers = Run(async _ =>
        {
            if (IsAdmin)
                Users = await api.Users();
        }, () => IsAdmin);
        Logout = Run(async _ =>
        {
            await api.Logout();
            ClientLog.Info("Выход пользователя");
            onLogout();
        });
    }

    public int Tab { get => tab; set => Set(ref tab, value); }
    public bool IsStaff => login?.User.Role is Roles.Admin or Roles.Operator;
    public bool IsAdmin => login?.User.Role == Roles.Admin;
    public string SessionCaption => login is null ? "" : $"{login.User.DisplayName} · {RoleName(login.User.Role)}";

    public string Search { get => search; set => Set(ref search, value); }
    public IReadOnlyList<StatusOption> Filters { get; } =
        [new StatusOption("Все этапы", null), .. Enum.GetValues<ShipmentStatus>().Select(status =>
            new StatusOption(StatusNames.Russian(status), status))];
    public StatusOption? SelectedFilter { get => selectedFilter; set => Set(ref selectedFilter, value); }

    public IReadOnlyList<ShipmentInfo> Shipments { get => shipments; private set => Set(ref shipments, value); }
    public ShipmentInfo? SelectedShipment { get => selectedShipment; set => Set(ref selectedShipment, value); }
    public int ShipmentCount => shipments.Count;

    public IReadOnlyList<RecipientInfo> Recipients { get => recipients; private set => Set(ref recipients, value); }
    public RecipientInfo? SelectedRecipient { get => selectedRecipient; set => Set(ref selectedRecipient, value); }
    public IReadOnlyList<UserInfo> Couriers { get => couriers; private set => Set(ref couriers, value); }
    public IReadOnlyList<UserInfo> Users { get => users; private set => Set(ref users, value); }
    public DeliveryStatistics Statistics { get => statistics; private set => Set(ref statistics, value); }
    public IReadOnlyList<StatusRow> StatusRows { get => statusRows; private set => Set(ref statusRows, value); }
    public string AverageDays => statistics.AverageDays is { } days
        ? days.ToString("0.00", CultureInfo.InvariantCulture) + " дн."
        : "—";
    public string AmountText => statistics.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
    public string DeliveredAmountText => statistics.DeliveredAmount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    public ICommand SetTab { get; }
    public ICommand RefreshShipments { get; }
    public ICommand NewShipment { get; }
    public ICommand EditShipment { get; }
    public ICommand DetailsShipment { get; }
    public ICommand Transition { get; }
    public ICommand Export { get; }
    public ICommand RefreshRecipients { get; }
    public ICommand NewRecipient { get; }
    public ICommand EditRecipient { get; }
    public ICommand Import { get; }
    public ICommand RefreshStatistics { get; }
    public ICommand RefreshUsers { get; }
    public ICommand Logout { get; }

    private async Task RefreshList()
    {
        Shipments = await api!.Shipments(SelectedFilter?.Value, Search);
        Changed(nameof(ShipmentCount));
    }

    private async Task RefreshRecipientsList() => Recipients = await api!.Recipients();

    private async Task RefreshStats()
    {
        Statistics = await api!.Statistics();
        StatusRows = statistics.ByStatus
            .Select(row => new StatusRow(StatusNames.Russian(row.Status), row.Count)).ToArray();
        Changed(nameof(AverageDays));
        Changed(nameof(AmountText));
        Changed(nameof(DeliveredAmountText));
    }

    private static string RoleName(string role) => role switch
    {
        Roles.Admin => "администратор",
        Roles.Operator => "оператор",
        _ => "курьер"
    };
}

public sealed record StatusRow(string Name, int Count);
