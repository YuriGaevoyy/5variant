using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using DeliveryService.Api;
using DeliveryService.Core;
using Microsoft.AspNetCore.Builder;

namespace DeliveryService.Desktop;

public partial class App : Application
{
    private WebApplication? host;
    private HttpClient? http;
    private Task<DeliveryClient>? client;
    private X509Certificate2? certificate;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ClientLog.Start();
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU")));
        client = Task.Run(StartClient);
        ShowLogin();
    }

    private async Task<DeliveryClient> StartClient()
    {
        certificate = LoopbackCertificate.Create();
        byte[] expected = certificate.GetCertHash(HashAlgorithmName.SHA256);
        host = DeliveryHost.Build([], certificate);
        await DeliveryHost.Initialize(host);
        await host.StartAsync();
        var transport = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (request, presented, _, _) =>
                request.RequestUri is { Scheme: "https", Host: "127.0.0.1" } &&
                presented is not null &&
                CryptographicOperations.FixedTimeEquals(expected, presented.GetCertHash(HashAlgorithmName.SHA256))
        };
        http = new HttpClient(transport)
        {
            BaseAddress = new Uri(host.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(12)
        };
        return new DeliveryClient(http);
    }

    private void ShowLogin()
    {
        var model = new LoginViewModel(client!, ShowWorkspace);
        SwitchTo(new LoginWindow { DataContext = model });
    }

    private void ShowWorkspace(DeliveryClient api, LoginResult login)
    {
        var shell = new WorkspaceWindow();
        var model = new WorkspaceViewModel(api, login, new DesktopDialogs(shell), ShowLogin);
        shell.DataContext = model;
        SwitchTo(shell);
        model.RefreshShipments.Execute(null);
    }

    private void SwitchTo(Window next)
    {
        var previous = MainWindow;
        MainWindow = next;
        next.Show();
        if (previous is not null && previous != next)
            previous.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (client is not null)
                Task.WhenAny(client, Task.Delay(1500)).GetAwaiter().GetResult();
            if (client?.IsCompleted == true && host is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                host.StopAsync(timeout.Token).GetAwaiter().GetResult();
                host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception error)
        {
            ClientLog.Write(error);
        }
        finally
        {
            http?.Dispose();
            if (client?.IsCompleted == true)
                certificate?.Dispose();
            ClientLog.Info("Завершение работы клиента службы доставки");
            base.OnExit(e);
        }
    }
}
