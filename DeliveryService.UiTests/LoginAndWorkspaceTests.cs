using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Xunit;

namespace DeliveryService.UiTests;

[Collection("WPF")]
public sealed class LoginAndWorkspaceTests
{
    [Theory]
    [InlineData("admin", "Admin@2026!", "Администратор · администратор", true, true)]
    [InlineData("operator", "Operator@2026!", "Оператор · оператор", true, false)]
    [InlineData("courier1", "Courier1@2026!", "Курьер Антон · курьер", false, false)]
    [InlineData("courier2", "Courier2@2026!", "Курьер Мария · курьер", false, false)]
    public void Login_shows_role_specific_workspace_and_logout_returns_to_login(
        string login, string password, string caption, bool staff, bool admin)
    {
        using var run = new DesktopRun();
        var window = run.Window("Служба доставки — вход");
        SetCredentials(window, login, password);
        Invoke(window, "SignInButton");

        var workspace = run.Window("Служба доставки");
        Assert.Equal(caption, Element(workspace, "SessionCaption").Name);
        Assert.Equal(staff, Visible(workspace, "NewShipmentButton"));
        Assert.Equal(staff, Visible(workspace, "NavRecipients"));
        Assert.Equal(admin, Visible(workspace, "NavUsers"));
        Assert.True(Visible(workspace, "NavShipments"));
        Assert.True(Visible(workspace, "NavStatistics"));
        Assert.True(Visible(workspace, "ShipmentSearchInput"));
        Assert.True(Visible(workspace, "StatusFilter"));
        Assert.True(Visible(workspace, "LogoutButton"));
        WaitForCount(workspace, login == "courier1" ? 3 : staff ? 9 : 2);

        Invoke(workspace, "LogoutButton");
        var returned = run.Window("Служба доставки — вход");
        Assert.True(Visible(returned, "SignInButton"));
    }

    [Fact]
    public void Wrong_password_keeps_login_window_open()
    {
        using var run = new DesktopRun();
        var window = run.Window("Служба доставки — вход");
        SetCredentials(window, "admin", "wrong-password");
        Invoke(window, "SignInButton");
        Assert.True(SpinWait.SpinUntil(
            () => Element(window, "LoginError").Name.Contains("Неверный логин", StringComparison.Ordinal),
            TimeSpan.FromSeconds(18)), "Сообщение об ошибке входа не появилось.");
    }

    [Fact]
    public void Search_changes_number_of_visible_shipments()
    {
        using var run = new DesktopRun();
        var login = run.Window("Служба доставки — вход");
        SetCredentials(login, "admin", "Admin@2026!");
        Invoke(login, "SignInButton");
        var workspace = run.Window("Служба доставки");
        WaitForCount(workspace, 9);

        Element(workspace, "ShipmentSearchInput").AsTextBox().Text = "несуществующий-адрес";
        WaitForCount(workspace, 0);

        Element(workspace, "ShipmentSearchInput").AsTextBox().Text = "";
        WaitForCount(workspace, 9);
    }

    [Fact]
    public void Status_filter_limits_courier_list_to_in_transit()
    {
        using var run = new DesktopRun();
        var login = run.Window("Служба доставки — вход");
        SetCredentials(login, "courier1", "Courier1@2026!");
        Invoke(login, "SignInButton");
        var workspace = run.Window("Служба доставки");
        WaitForCount(workspace, 3);
        Element(workspace, "StatusFilter").AsComboBox().Select("В пути");
        WaitForCount(workspace, 1);
    }

    [Fact]
    public void Minimal_window_keeps_navigation_and_actions_visible()
    {
        using var run = new DesktopRun();
        var window = run.Window("Служба доставки — вход");
        SetCredentials(window, "operator", "Operator@2026!");
        Invoke(window, "SignInButton");
        var workspace = run.Window("Служба доставки");
        Assert.True(Visible(workspace, "NavShipments"));
        Assert.True(Visible(workspace, "NavRecipients"));
        Assert.True(Visible(workspace, "NavStatistics"));
        Assert.True(Visible(workspace, "NewShipmentButton"));
        Assert.True(Visible(workspace, "TransitionButton"));
    }

    private static void WaitForCount(Window window, int expected)
    {
        var count = Element(window, "ShipmentCount");
        Assert.True(SpinWait.SpinUntil(() => count.Name == $"В списке: {expected}",
            TimeSpan.FromSeconds(18)), $"Ожидалось {expected} отправлений, получено «{count.Name}».");
    }

    private static void SetCredentials(Window window, string login, string password)
    {
        Element(window, "LoginInput").AsTextBox().Text = login;
        var field = Element(window, "PasswordInput");
        try
        {
            field.Patterns.Value.Pattern.SetValue(password);
        }
        catch (Exception error) when (error is PatternNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            window.Focus();
            field.Focus();
            Keyboard.Type(password);
        }
    }

    private static void Invoke(Window window, string id)
    {
        var element = Element(window, id);
        Assert.True(SpinWait.SpinUntil(() => element.IsEnabled, TimeSpan.FromSeconds(10)));
        element.AsButton().Invoke();
    }

    private static AutomationElement Element(Window window, string id) =>
        window.FindFirstDescendant(condition => condition.ByAutomationId(id)) ??
        throw new InvalidOperationException($"Элемент UIA '{id}' отсутствует.");

    private static bool Visible(Window window, string id) =>
        window.FindFirstDescendant(condition => condition.ByAutomationId(id)) is { IsOffscreen: false };

    private sealed class DesktopRun : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "DeliveryService.UiTests",
            Guid.NewGuid().ToString("N"));
        private readonly UIA3Automation automation = new();
        private readonly Process process;

        public DesktopRun()
        {
            var output = new DirectoryInfo(AppContext.BaseDirectory);
            var root = output;
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "DeliveryService.slnx")))
                root = root.Parent;
            var executable = Environment.GetEnvironmentVariable("DELIVERY_UI_EXECUTABLE") ??
                Path.Combine(root?.FullName ?? throw new InvalidOperationException("Решение не найдено."),
                    "DeliveryService.Desktop", "bin", output.Parent!.Name, "net10.0-windows",
                    "DeliveryService.Desktop.exe");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            start.Environment["Delivery__Database"] = Path.Combine(directory, "ui.db");
            start.Environment["Delivery__SeedDemo"] = "true";
            process = Process.Start(start) ?? throw new InvalidOperationException("WPF-клиент не запустился.");
        }

        public Window Window(string title)
        {
            var desktop = automation.GetDesktop();
            AutomationElement? element = null;
            bool ready = SpinWait.SpinUntil(() =>
                (element = desktop.FindFirstChild(cf => cf.ByProcessId(process.Id).And(cf.ByName(title)))) is not null,
                TimeSpan.FromSeconds(25));
            if (!ready)
                throw new InvalidOperationException($"Окно «{title}» не открылось.");
            return element!.AsWindow();
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                    process.CloseMainWindow();
                if (!process.HasExited && !process.WaitForExit(4000))
                    process.Kill(entireProcessTree: true);
                if (!process.HasExited)
                    process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершён.
            }
            finally
            {
                automation.Dispose();
                if (Directory.Exists(directory))
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    for (int attempt = 0; attempt < 5 && Directory.Exists(directory); attempt++)
                    {
                        try
                        {
                            Directory.Delete(directory, recursive: true);
                        }
                        catch (IOException)
                        {
                            Thread.Sleep(300);
                        }
                    }
                }
            }
        }
    }
}

[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection;
