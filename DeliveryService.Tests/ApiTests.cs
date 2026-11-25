using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DeliveryService.Api;
using DeliveryService.Core;
using DeliveryService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DeliveryService.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        using var server = new DeliveryFactory();
        using var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/shipments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/statistics")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        using var server = new DeliveryFactory();
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task All_demo_roles_can_sign_in()
    {
        using var server = new DeliveryFactory();
        using var admin = await SignIn(server, "admin", "Admin@2026!", Roles.Admin);
        using var operatorClient = await SignIn(server, "operator", "Operator@2026!", Roles.Operator);
        using var first = await SignIn(server, "courier1", "Courier1@2026!", Roles.Courier);
        using var second = await SignIn(server, "courier2", "Courier2@2026!", Roles.Courier);
        Assert.Equal(Roles.Admin, (await Get<UserInfo>(admin, "/api/auth/me")).Role);
        Assert.Equal(Roles.Operator, (await Get<UserInfo>(operatorClient, "/api/auth/me")).Role);
        Assert.Equal("Курьер Антон", (await Get<UserInfo>(first, "/api/auth/me")).DisplayName);
        Assert.Equal("Курьер Мария", (await Get<UserInfo>(second, "/api/auth/me")).DisplayName);
    }

    [Fact]
    public async Task Couriers_see_only_assigned_shipments_in_list_card_and_statistics()
    {
        using var server = new DeliveryFactory();
        using var first = await SignIn(server, "courier1", "Courier1@2026!", Roles.Courier);
        using var second = await SignIn(server, "courier2", "Courier2@2026!", Roles.Courier);
        var firstRows = await Get<ShipmentInfo[]>(first, "/api/shipments");
        var secondRows = await Get<ShipmentInfo[]>(second, "/api/shipments");
        Assert.NotEmpty(firstRows);
        Assert.NotEmpty(secondRows);
        Assert.All(firstRows, row => Assert.Equal(3, row.CourierId));
        Assert.All(secondRows, row => Assert.Equal(4, row.CourierId));
        Assert.DoesNotContain(firstRows, row => secondRows.Any(other => other.Id == row.Id));
        Assert.Equal(firstRows.Length, (await Get<DeliveryStatistics>(first, "/api/statistics")).Total);
        Assert.Equal(HttpStatusCode.NotFound,
            (await first.GetAsync($"/api/shipments/{secondRows[0].Id}")).StatusCode);
    }

    [Fact]
    public async Task Role_permissions_are_enforced_for_staff_endpoints()
    {
        using var server = new DeliveryFactory();
        using var admin = await SignIn(server, "admin", "Admin@2026!", Roles.Admin);
        using var operatorClient = await SignIn(server, "operator", "Operator@2026!", Roles.Operator);
        using var courier = await SignIn(server, "courier1", "Courier1@2026!", Roles.Courier);
        Assert.Equal(4, (await Get<UserInfo[]>(admin, "/api/users")).Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.GetAsync("/api/recipients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await courier.GetAsync("/api/couriers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            await Post(courier, "/api/shipments", new { }));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await courier.GetAsync("/api/shipments/export?format=json")).StatusCode);
        Assert.Equal(2, (await Get<UserInfo[]>(operatorClient, "/api/couriers")).Length);
    }

    [Fact]
    public async Task Courier_cannot_force_a_stage_outside_his_scope()
    {
        using var server = new DeliveryFactory();
        using var courier = await SignIn(server, "courier1", "Courier1@2026!", Roles.Courier);
        var row = (await Get<ShipmentInfo[]>(courier, "/api/shipments"))
            .First(item => item.Status == ShipmentStatus.InTransit);
        Assert.Equal(HttpStatusCode.Forbidden, await Post(courier, $"/api/shipments/{row.Id}/transition",
            new TransitionInput(ShipmentStatus.Accepted, null, null, row.Version)));
        Assert.Equal(HttpStatusCode.NotFound, await Post(courier, "/api/shipments/99999/transition",
            new TransitionInput(ShipmentStatus.Delivered, null, "Получатель", row.Version)));
    }

    [Fact]
    public async Task Lifecycle_rules_are_validated_on_the_server()
    {
        using var server = new DeliveryFactory();
        using var staff = await SignIn(server, "operator", "Operator@2026!", Roles.Operator);
        int recipient = (await Get<RecipientInfo[]>(staff, "/api/recipients"))[0].Id;
        int courier = (await Get<UserInfo[]>(staff, "/api/couriers"))[0].Id;
        int id = await Create(staff, new ShipmentInput(recipient, "Тестовая посылка", "ул. Тестовая, 1",
            1.5m, 1200m, null, ""));
        string path = $"/api/shipments/{id}";
        var state = await Get<ShipmentInfo>(staff, path);
        Assert.Equal(ShipmentStatus.Created, state.Status);

        Assert.Equal(HttpStatusCode.BadRequest, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.InTransit, null, null, state.Version)));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.Delivered, null, "Рано", state.Version)));
        state = await Get<ShipmentInfo>(staff, path);
        using (var assign = await staff.PutAsJsonAsync(path,
            state with { CourierId = courier, Version = state.Version }))
            Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        state = await Get<ShipmentInfo>(staff, path);
        Assert.Equal(HttpStatusCode.NoContent, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.Accepted, null, null, state.Version)));
        state = await Get<ShipmentInfo>(staff, path);
        Assert.Equal(HttpStatusCode.NoContent, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.InTransit, null, null, state.Version)));
        state = await Get<ShipmentInfo>(staff, path);
        Assert.Equal(HttpStatusCode.BadRequest, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.Delivered, null, "  ", state.Version)));
        Assert.Equal(HttpStatusCode.NoContent, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.Delivered, null, "Получено", state.Version)));
        state = await Get<ShipmentInfo>(staff, path);
        Assert.Equal(ShipmentStatus.Delivered, state.Status);
        Assert.NotNull(state.DeliveredAt);
        Assert.Equal(HttpStatusCode.NoContent, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.Returned, "Отказ получателя", null, state.Version)));
        state = await Get<ShipmentInfo>(staff, path);
        Assert.Equal(HttpStatusCode.BadRequest, await Post(staff, path + "/transition",
            new TransitionInput(ShipmentStatus.Accepted, null, null, state.Version)));
    }

    [Fact]
    public async Task Stale_version_returns_conflict()
    {
        using var server = new DeliveryFactory();
        using var staff = await SignIn(server, "operator", "Operator@2026!", Roles.Operator);
        var row = (await Get<ShipmentInfo[]>(staff, "/api/shipments"))[0];
        using var edit = await staff.PutAsJsonAsync($"/api/shipments/{row.Id}",
            row with { Version = row.Version - 1 });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task Search_and_status_filter_reduce_result_set()
    {
        using var server = new DeliveryFactory();
        using var staff = await SignIn(server, "admin", "Admin@2026!", Roles.Admin);
        var all = await Get<ShipmentInfo[]>(staff, "/api/shipments");
        Assert.True(all.Length >= 9);
        var created = await Get<ShipmentInfo[]>(staff, "/api/shipments?status=Created");
        Assert.NotEmpty(created);
        Assert.All(created, row => Assert.Equal(ShipmentStatus.Created, row.Status));
        var searched = await Get<ShipmentInfo[]>(staff,
            $"/api/shipments?search={Uri.EscapeDataString(created[0].Recipient)}");
        Assert.NotEmpty(searched);
        Assert.Contains(searched, row => row.Id == created[0].Id);
    }

    [Fact]
    public async Task Csv_import_reports_error_lines_and_imports_valid_rows()
    {
        using var server = new DeliveryFactory();
        using var staff = await SignIn(server, "operator", "Operator@2026!", Roles.Operator);
        const string badCsv = "Name,Phone,Address,Email\nИван,+79990000000,ул. Новая,test@example.test\n,123,ул. Старая,x@example.test";
        using var bad = await staff.PostAsJsonAsync("/api/recipients/import", new { csv = badCsv });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        string body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("Строка 3", body, StringComparison.Ordinal);

        const string goodCsv = "Name,Phone,Address,Email\nПётр,+79991112233,\"ул. Садовая, 10\",p@example.test";
        using var good = await staff.PostAsJsonAsync("/api/recipients/import", new { csv = goodCsv });
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        Assert.Equal(1, (await good.Content.ReadFromJsonAsync<ImportResult>(Exchange.JsonOptions))!.Imported);
    }

    [Fact]
    public async Task Export_supports_csv_json_and_xml()
    {
        using var server = new DeliveryFactory();
        using var staff = await SignIn(server, "operator", "Operator@2026!", Roles.Operator);
        string csv = await (await staff.GetAsync("/api/shipments/export?format=csv")).Content.ReadAsStringAsync();
        string json = await (await staff.GetAsync("/api/shipments/export?format=json")).Content.ReadAsStringAsync();
        string xml = await (await staff.GetAsync("/api/shipments/export?format=xml")).Content.ReadAsStringAsync();
        Assert.Contains("Number;Recipient", csv, StringComparison.Ordinal);
        Assert.Contains("\"status\"", json, StringComparison.Ordinal);
        Assert.Contains("<shipments>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_revokes_token()
    {
        using var server = new DeliveryFactory();
        using var courier = await SignIn(server, "courier2", "Courier2@2026!", Roles.Courier);
        Assert.Equal(HttpStatusCode.OK, (await courier.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await courier.GetAsync("/api/shipments")).StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        using var server = new DeliveryFactory();
        using var client = server.CreateClient();
        var health = await Get<HealthInfo>(client, "/health");
        Assert.Equal("ok", health.Status);
        Assert.True(health.Shipments >= 9);
    }

    private static async Task<HttpClient> SignIn(WebApplicationFactory<Program> server, string login,
        string password, string role)
    {
        var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginInput(login, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<LoginResult>(Exchange.JsonOptions))!;
        Assert.Equal(role, result.User.Role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        return client;
    }

    private static async Task<T> Get<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Exchange.JsonOptions))!;
    }

    private static async Task<HttpStatusCode> Post(HttpClient client, string url, object value)
    {
        using var response = await client.PostAsJsonAsync(url, value);
        return response.StatusCode;
    }

    private static async Task<int> Create(HttpClient client, ShipmentInput input)
    {
        using var response = await client.PostAsJsonAsync("/api/shipments", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ShipmentInfo>(Exchange.JsonOptions))!.Id;
    }

    private sealed class DeliveryFactory : WebApplicationFactory<Program>
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "DeliveryService.Tests",
            Guid.NewGuid().ToString("N"));

        protected override IHost CreateHost(IHostBuilder builder)
        {
            Set("Delivery__Database", Path.Combine(directory, "integration.db"));
            Set("Delivery__SeedDemo", "true");
            Set("urls", "https://localhost:7135");
            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                Set("Delivery__Database", null);
                Set("Delivery__SeedDemo", null);
                Set("urls", null);
            }
        }

        private static void Set(string name, string? value) => Environment.SetEnvironmentVariable(name, value);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory))
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
