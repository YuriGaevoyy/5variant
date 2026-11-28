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
    private CancellationTokenSource? filterRequest;
    private int shipmentQuery;
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
        selectedFilter = Filters[0];
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
        selectedFilter = Filters[0];

        SetTab = Run(async parameter =>
        {
            Tab = System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
            switch (Tab)
            {
                case 0: await RefreshList(); break;
                case 1 when IsStaff: await RefreshRecipientsAndCouriers(); break;
                case 2: await RefreshStats(); break;
                case 3 when IsAdmin: await RefreshUserList(); break;
            }
        });
        RefreshShipments = Run(_ => RefreshList());
        NewShipment = Run(async _ =>
        {
            await RefreshRecipientsAndCouriers();
            if (recipients.Count == 0)
                throw new RuleException("Сначала добавьте получателя в разделе «Получатели».");
            var input = dialogs.EditShipment(null, recipients, couriers);
            if (input is null)
                return;
            await api.SaveShipment(null, input);
            Message = "Отправление создано.";
            await RefreshList();
        }, () => IsStaff);
        EditShipment = Run(async _ =>
        {
            if (selectedShipment is not { } selected)
                throw new RuleException("Выберите отправление.");
            await RefreshRecipientsAndCouriers();
            var input = dialogs.EditShipment(selected, recipients, couriers);
            if (input is null)
                return;
            await api.SaveShipment(selected.Id, input with { Version = selected.Version });
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
        }, () => selectedShipment is not null && ShipmentRules.Next(selectedShipment.Status)
            .Any(status => IsStaff || status is ShipmentStatus.Delivered or ShipmentStatus.Returned));
        Export = Run(async parameter =>
        {
            string format = parameter?.ToString() ?? "csv";
            byte[] bytes = await api.Export(format, SelectedFilter?.Value, Search);
            dialogs.SaveExport(format, bytes);
            Message = $"Выгрузка {format.ToUpperInvariant()} сохранена.";
        }, () => IsStaff);
        RefreshRecipients = Run(_ => RefreshRecipientsAndCouriers(), () => IsStaff);
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
        RefreshUsers = Run(_ => RefreshUserList(), () => IsAdmin);
        Logout = Run(async _ =>
        {
            filterRequest?.Cancel();
            try
            {
                await api.Logout();
            }
            finally
            {
                ClientLog.Info("Выход пользователя");
                onLogout();
            }
        });
    }

    public int Tab { get => tab; set => Set(ref tab, value); }
    public bool IsStaff => login?.User.Role is Roles.Admin or Roles.Operator;
    public bool IsAdmin => login?.User.Role == Roles.Admin;
    public string SessionCaption => login is null ? "" : $"{login.User.DisplayName} · {RoleName(login.User.Role)}";

    public string Search
    {
        get => search;
        set
        {
            if (search == value)
                return;
            Set(ref search, value);
            _ = RefreshFilter(delay: true);
        }
    }
    public IReadOnlyList<StatusOption> Filters { get; } =
        [new StatusOption("Все этапы", null), .. Enum.GetValues<ShipmentStatus>().Select(status =>
            new StatusOption(StatusNames.Russian(status), status))];
    public StatusOption? SelectedFilter
    {
        get => selectedFilter;
        set
        {
            if (selectedFilter == value)
                return;
            Set(ref selectedFilter, value);
            _ = RefreshFilter(delay: false);
        }
    }

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

    private async Task RefreshFilter(bool delay)
    {
        if (api is null)
            return;
        shipmentQuery++;
        filterRequest?.Cancel();
        var request = new CancellationTokenSource();
        filterRequest = request;
        try
        {
            if (delay)
                await Task.Delay(250, request.Token);
            await RefreshList(request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (ClientException error)
        {
            if (!request.IsCancellationRequested)
                Message = error.Message;
        }
        catch (Exception error)
        {
            ClientLog.Write(error);
            if (!request.IsCancellationRequested)
                Message = "Не удалось обновить список отправлений.";
        }
        finally
        {
            if (filterRequest == request)
                filterRequest = null;
            request.Dispose();
        }
    }

    private async Task RefreshList(CancellationToken cancellationToken = default)
    {
        int query = ++shipmentQuery;
        var result = await api!.Shipments(SelectedFilter?.Value, Search, cancellationToken);
        if (query != shipmentQuery || cancellationToken.IsCancellationRequested)
            return;
        int? selectedId = selectedShipment?.Id;
        Shipments = result;
        SelectedShipment = result.FirstOrDefault(row => row.Id == selectedId);
        Changed(nameof(ShipmentCount));
    }

    private async Task RefreshRecipientsAndCouriers()
    {
        Recipients = await api!.Recipients();
        Couriers = await api.Couriers();
    }

    private async Task RefreshRecipientsList() => Recipients = await api!.Recipients();

    private async Task RefreshUserList() => Users = await api!.Users();

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
