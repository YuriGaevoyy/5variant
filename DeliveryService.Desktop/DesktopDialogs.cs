using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DeliveryService.Core;
using Microsoft.Win32;

namespace DeliveryService.Desktop;

internal interface IDialogService
{
    ShipmentInput? EditShipment(ShipmentInfo? current, IReadOnlyList<RecipientInfo> recipients,
        IReadOnlyList<UserInfo> couriers);
    RecipientInput? EditRecipient(RecipientInfo? current);
    TransitionInput? ChooseTransition(ShipmentInfo current, bool staff);
    void ShowDetails(ShipmentInfo shipment);
    string? PickCsv();
    void SaveExport(string format, byte[] bytes);
}

internal sealed class DesktopDialogs : IDialogService
{
    private readonly Window owner;

    public DesktopDialogs(Window owner) => this.owner = owner;

    public ShipmentInput? EditShipment(ShipmentInfo? current, IReadOnlyList<RecipientInfo> recipients,
        IReadOnlyList<UserInfo> couriers)
    {
        var dialog = Frame(current is null ? "Новое отправление" : "Редактирование отправления", 520, 590);
        var grid = Fields(dialog);
        int row = 0;

        var recipientValues = recipients.ToDictionary(item => $"{item.Name} · {item.Phone} (№ {item.Id})", item => (int?)item.Id);
        var recipient = Combo("Получатель", recipientValues.Keys.ToArray(), recipientValues,
            current?.RecipientId, row++, "RecipientCombo");
        var description = Text("Описание", current?.Description ?? "", row++, "DescriptionInput");
        var address = Text("Адрес доставки", current?.DeliveryAddress ?? "", row++, "AddressInput");
        var weight = Text("Вес, кг", current?.WeightKg.ToString("0.00", CultureInfo.InvariantCulture) ?? "1,00", row++, "WeightInput");
        var amount = Text("Объявленная сумма, ₽",
            current?.DeclaredAmount.ToString("0.00", CultureInfo.InvariantCulture) ?? "1000,00", row++, "AmountInput");
        var courierItems = new Dictionary<string, int?> { ["— не назначен —"] = null };
        foreach (var item in couriers)
            courierItems[$"{item.DisplayName} ({item.Login})"] = item.Id;
        var courier = Combo("Курьер", courierItems.Keys.ToArray(), courierItems, current?.CourierId, row++, "CourierCombo");
        var notes = Text("Примечания", current?.Notes ?? "", row++, "NotesInput");

        if (!Finish(dialog, grid))
            return null;
        try
        {
            return new ShipmentInput(
                recipient() ?? throw new RuleException("Выберите получателя."),
                InputRules.Required(description(), "Описание", 180),
                InputRules.Required(address(), "Адрес доставки", 220),
                InputRules.ParseWeight(weight()),
                InputRules.ParseAmount(amount()), courier(),
                InputRules.Optional(notes(), "Примечания", 2000), current?.Version);
        }
        catch (RuleException error)
        {
            MessageBox.Show(owner, error.Message, "Проверьте данные", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    public RecipientInput? EditRecipient(RecipientInfo? current)
    {
        var dialog = Frame(current is null ? "Новый получатель" : "Редактирование получателя", 470, 440);
        var grid = Fields(dialog);
        var name = Text("Получатель", current?.Name ?? "", 0, "RecipientNameInput");
        var phone = Text("Телефон", current?.Phone ?? "", 1, "RecipientPhoneInput");
        var address = Text("Адрес", current?.Address ?? "", 2, "RecipientAddressInput");
        var email = Text("Эл. почта", current?.Email ?? "", 3, "RecipientEmailInput");
        if (!Finish(dialog, grid))
            return null;
        try
        {
            return InputRules.Recipient(new RecipientInput(name(), phone(), address(), email()));
        }
        catch (RuleException error)
        {
            MessageBox.Show(owner, error.Message, "Проверьте данные", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    public TransitionInput? ChooseTransition(ShipmentInfo current, bool staff)
    {
        var dialog = Frame("Смена этапа отправления " + current.TrackingNumber, 490, 470);
        var grid = Fields(dialog);
        var allowed = ShipmentRules.Next(current.Status)
            .Where(status => staff || status is ShipmentStatus.Delivered or ShipmentStatus.Returned)
            .ToArray();
        var items = allowed.ToDictionary(StatusNames.Russian, status => (ShipmentStatus?)status);
        var status = Combo("Новый этап", items.Keys.ToArray(), items, null, 0, "TargetStatusCombo");
        var reason = Text("Причина возврата", "", 1, "ReturnReasonInput");
        var confirmation = Text("Подтверждение получения", current.Confirmation ?? "", 2, "ConfirmationInput");
        if (!Finish(dialog, grid))
            return null;
        try
        {
            var target = status() ?? throw new RuleException("Выберите новый этап.");
            return new TransitionInput(target, reason(), confirmation(), current.Version);
        }
        catch (RuleException error)
        {
            MessageBox.Show(owner, error.Message, "Проверьте данные", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    public void ShowDetails(ShipmentInfo shipment) => MessageBox.Show(owner,
        $"{shipment.TrackingNumber} · {StatusNames.Russian(shipment.Status)}{Environment.NewLine}" +
        $"Получатель: {shipment.Recipient}, {shipment.RecipientPhone}{Environment.NewLine}" +
        $"Адрес: {shipment.DeliveryAddress}{Environment.NewLine}" +
        $"Описание: {shipment.Description}{Environment.NewLine}" +
        $"Курьер: {shipment.Courier ?? "не назначен"}{Environment.NewLine}" +
        $"Сумма: {shipment.DeclaredAmount:N2} ₽ · Вес: {shipment.WeightKg:0.00} кг{Environment.NewLine}" +
        $"Создано: {shipment.CreatedAt:dd.MM.yyyy HH:mm}{Environment.NewLine}" +
        $"Доставлено: {shipment.DeliveredAt?.ToString("dd.MM.yyyy HH:mm") ?? "—"}{Environment.NewLine}" +
        $"Возврат: {shipment.ReturnReason ?? "—"}",
        "Карточка отправления", MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PickCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт получателей из CSV",
            Filter = "CSV-файлы (*.csv)|*.csv|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        return dialog.ShowDialog(owner) == true ? File.ReadAllText(dialog.FileName, Encoding.UTF8) : null;
    }

    public void SaveExport(string format, byte[] bytes)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить выгрузку отправлений",
            FileName = $"shippings.{format}",
            Filter = format switch
            {
                "json" => "JSON (*.json)|*.json",
                "xml" => "XML (*.xml)|*.xml",
                _ => "CSV (*.csv)|*.csv"
            }
        };
        if (dialog.ShowDialog(owner) == true)
            File.WriteAllBytes(dialog.FileName, bytes);
    }

    private Window Frame(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = width - 60,
        MinHeight = height - 60,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = (Brush)Application.Current.Resources["Canvas"],
        FontFamily = new FontFamily("Segoe UI"),
        Owner = owner,
        ResizeMode = ResizeMode.NoResize
    };

    private static Grid Fields(Window dialog)
    {
        var grid = new Grid { Margin = new Thickness(20) };
        dialog.Content = grid;
        CurrentGrid = grid;
        return grid;
    }

    private static Func<string> Text(string label, string initial, int row, string automationId)
    {
        var box = new TextBox
        {
            Text = initial,
            Margin = new Thickness(150, row * 46, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Height = 34
        };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, label);
        CurrentGrid!.Children.Add(Label(label, row));
        CurrentGrid.Children.Add(box);
        return () => box.Text;
    }

    private static Func<TValue?> Combo<TValue>(string label, IReadOnlyList<string> items,
        IReadOnlyDictionary<string, TValue> values, TValue? selected, int row, string automationId)
    {
        var box = new ComboBox
        {
            ItemsSource = items,
            Margin = new Thickness(150, row * 46, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Height = 34
        };
        if (selected is not null &&
            values.FirstOrDefault(pair => EqualityComparer<TValue>.Default.Equals(pair.Value, selected)).Key is { } key)
            box.SelectedItem = key;
        else if (items.Count > 0)
            box.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, label);
        CurrentGrid!.Children.Add(Label(label, row));
        CurrentGrid.Children.Add(box);
        return () => box.SelectedItem is string text && values.TryGetValue(text, out var value) ? value : default;
    }

    private static Label Label(string text, int row) => new()
    {
        Content = text,
        Margin = new Thickness(0, row * 46 + 6, 0, 0),
        VerticalAlignment = VerticalAlignment.Top,
        Width = 145,
        Foreground = (Brush)Application.Current.Resources["Muted"]
    };

    [ThreadStatic]
    private static Grid? CurrentGrid;

    private static bool Finish(Window dialog, Grid grid)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var ok = new Button
        {
            Content = "Сохранить",
            Width = 110,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            IsDefault = true
        };
        AutomationProperties.SetAutomationId(ok, "DialogSave");
        var cancel = new Button
        {
            Content = "Отмена",
            Width = 90,
            Height = 34,
            Style = (Style)Application.Current.Resources["SoftButton"],
            IsCancel = true
        };
        AutomationProperties.SetAutomationId(cancel, "DialogCancel");
        ok.Click += (_, _) => dialog.DialogResult = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        grid.Children.Add(buttons);
        return dialog.ShowDialog() == true;
    }
}
