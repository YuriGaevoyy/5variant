using DeliveryService.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryService.Api;

[ApiController]
[Route("api")]
[Authorize]
public sealed class DirectoriesController(
    IRecipientRepository recipients,
    IUserRepository users,
    IShipmentRepository shipments) : ControllerBase
{
    [HttpGet("recipients")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<IReadOnlyList<RecipientInfo>>> Recipients(CancellationToken ct) =>
        Ok(await recipients.GetAll(ct));

    [HttpPost("recipients")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<RecipientInfo>> CreateRecipient([FromBody] RecipientInput input, CancellationToken ct)
    {
        int id = await recipients.Create(input, ct);
        var all = await recipients.GetAll(ct);
        return CreatedAtAction(nameof(Recipients), all.Single(row => row.Id == id));
    }

    [HttpPut("recipients/{id:int}")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> UpdateRecipient(int id, [FromBody] RecipientInput input, CancellationToken ct) =>
        await recipients.Update(id, input, ct) ? NoContent() : NotFound();

    [HttpPost("recipients/import")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<ImportResult>> Import([FromBody] CsvInput input, CancellationToken ct) =>
        Ok(await recipients.Import(input.Csv, ct));

    [HttpGet("couriers")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<IReadOnlyList<UserInfo>>> Couriers(CancellationToken ct) =>
        Ok(await users.GetAll(true, ct));

    [HttpGet("users")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IReadOnlyList<UserInfo>>> Users(CancellationToken ct) =>
        Ok(await users.GetAll(false, ct));

    [HttpGet("statistics")]
    public async Task<ActionResult<DeliveryStatistics>> Statistics(CancellationToken ct)
    {
        var metrics = await shipments.FindMetrics(SessionTokens.Current(User), ct);
        return Ok(ShipmentRules.StatisticsFromMetrics(metrics));
    }
}

public sealed record CsvInput(string Csv);
