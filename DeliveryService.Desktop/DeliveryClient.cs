using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeliveryService.Core;

namespace DeliveryService.Desktop;

internal sealed class DeliveryClient(HttpClient http)
{
    private string? token;

    public async Task<LoginResult> Login(string login, string password)
    {
        var result = await Json<LoginResult>(HttpMethod.Post, "api/auth/login", new LoginInput(login, password));
        token = result.Token;
        return result;
    }

    public async Task Logout()
    {
        try
        {
            await Send(HttpMethod.Post, "api/auth/logout");
        }
        finally
        {
            token = null;
        }
    }

    public Task<ShipmentInfo[]> Shipments(ShipmentStatus? status, string? search) =>
        Json<ShipmentInfo[]>(HttpMethod.Get, ShipmentPath(status, search, "api/shipments"));

    public Task<RecipientInfo[]> Recipients() => Json<RecipientInfo[]>(HttpMethod.Get, "api/recipients");

    public Task<UserInfo[]> Couriers() => Json<UserInfo[]>(HttpMethod.Get, "api/couriers");

    public Task<UserInfo[]> Users() => Json<UserInfo[]>(HttpMethod.Get, "api/users");

    public Task<DeliveryStatistics> Statistics() => Json<DeliveryStatistics>(HttpMethod.Get, "api/statistics");

    public Task SaveShipment(int? id, ShipmentInput shipment) =>
        id.HasValue ? Send(HttpMethod.Put, $"api/shipments/{id}", shipment) : Send(HttpMethod.Post, "api/shipments", shipment);

    public Task SaveRecipient(int? id, RecipientInput recipient) =>
        id.HasValue ? Send(HttpMethod.Put, $"api/recipients/{id}", recipient) : Send(HttpMethod.Post, "api/recipients", recipient);

    public Task Transition(int id, TransitionInput input) =>
        Send(HttpMethod.Post, $"api/shipments/{id}/transition", input);

    public async Task<ImportResult> Import(string csv)
    {
        using var response = await Request(HttpMethod.Post, "api/recipients/import",
            JsonContent.Create(new { csv }));
        return (await response.Content.ReadFromJsonAsync<ImportResult>(Exchange.JsonOptions))!;
    }

    public async Task<byte[]> Export(string format, ShipmentStatus? status, string? search)
    {
        using var response = await Request(HttpMethod.Get,
            ShipmentPath(status, search, $"api/shipments/export?format={format}"));
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string ShipmentPath(ShipmentStatus? status, string? search, string path)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (status.HasValue)
            query.Add($"status={status.Value}");
        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }

    private async Task Send(HttpMethod method, string path, object? input = null)
    {
        using var response = await Request(method, path, Content(input));
    }

    private async Task<T> Json<T>(HttpMethod method, string path, object? input = null)
    {
        using var response = await Request(method, path, Content(input));
        return await response.Content.ReadFromJsonAsync<T>(Exchange.JsonOptions) ??
            throw new ClientException("Сервер вернул пустой ответ.");
    }

    private async Task<HttpResponseMessage> Request(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (TaskCanceledException error)
        {
            ClientLog.Write(error, "Тайм-аут API");
            throw new ClientException("Истекло время ожидания локального сервера. Повторите действие.");
        }
        catch (HttpRequestException error)
        {
            ClientLog.Write(error, "Связь с API");
            throw new ClientException("Локальный сервер не ответил. Повторите действие.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            if (path == "api/recipients/import" && response.StatusCode == HttpStatusCode.BadRequest)
            {
                var report = await response.Content.ReadFromJsonAsync<CsvProblem>(Exchange.JsonOptions);
                if (report?.Errors is { Length: > 0 } errors)
                    throw new CsvImportException(errors);
                throw new ClientException(report?.Detail ?? report?.Title ?? "Не удалось прочитать CSV.");
            }
            string message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized when path == "api/auth/login" => "Неверный логин или пароль.",
                HttpStatusCode.Unauthorized => "Сеанс завершён. Выйдите и войдите заново.",
                HttpStatusCode.Forbidden => "У вашей роли нет доступа к этому действию.",
                HttpStatusCode.NotFound => "Запись недоступна. Обновите список.",
                HttpStatusCode.TooManyRequests => "Слишком много попыток. Повторите вход через минуту.",
                _ => await ReadProblem(response)
            };
            throw new ClientException(message);
        }
    }

    private static async Task<string> ReadProblem(HttpResponseMessage response)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { } message)
                return message;
            if (json.RootElement.TryGetProperty("title", out var title) && title.GetString() is { } summary)
                return summary;
        }
        catch (JsonException)
        {
            // Some framework responses have an empty body.
        }
        return $"Не удалось выполнить действие. Код ответа {(int)response.StatusCode}.";
    }

    private static HttpContent? Content(object? input) =>
        input is null ? null : JsonContent.Create(input, options: Exchange.JsonOptions);

    private sealed record CsvProblem(string? Title, string? Detail, CsvRowError[]? Errors);
}

internal sealed class ClientException(string message) : Exception(message);
