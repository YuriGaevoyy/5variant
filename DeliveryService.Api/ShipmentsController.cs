using System.Text;
using DeliveryService.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryService.Api;

[ApiController]
[Route("api/shipments")]
[Authorize]
public sealed class ShipmentsController(IShipmentRepository shipments) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShipmentInfo>>> List(
        [FromQuery] ShipmentStatus? status, [FromQuery] string? search, CancellationToken ct) =>
        Ok(await shipments.FindShipments(SessionTokens.Current(User), status, search, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShipmentInfo>> Get(int id, CancellationToken ct)
    {
        var shipment = await shipments.FindShipment(id, SessionTokens.Current(User), ct);
        return shipment is null ? NotFound() : Ok(shipment);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<ShipmentInfo>> Create([FromBody] ShipmentInput input, CancellationToken ct)
    {
        int id = await shipments.Create(input, SessionTokens.Current(User), ct);
        var created = await shipments.FindShipment(id, SessionTokens.Current(User), ct);
        return CreatedAtAction(nameof(Get), new { id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Update(int id, [FromBody] ShipmentInput input, CancellationToken ct)
    {
        bool updated = await shipments.Update(id, input, SessionTokens.Current(User), ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/transition")]
    public async Task<IActionResult> Transition(int id, [FromBody] TransitionInput input, CancellationToken ct)
    {
        // Курьер завершает или возвращает только закреплённые за ним отправления.
        if (SessionTokens.Current(User).Role == Roles.Courier &&
            input.Status is not (ShipmentStatus.Delivered or ShipmentStatus.Returned))
            return Forbid();

        bool changed = await shipments.Transition(id, SessionTokens.Current(User), input, ct);
        return changed ? NoContent() : NotFound();
    }

    [HttpGet("export")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Export([FromQuery] string format, CancellationToken ct)
    {
        var rows = await shipments.FindShipments(SessionTokens.Current(User), null, null, ct);
        string body = format.ToLowerInvariant() switch
        {
            "json" => Exchange.ExportJson(rows),
            "xml" => Exchange.ExportXml(rows),
            _ => Exchange.ExportCsv(rows)
        };
        string type = format.ToLowerInvariant() switch
        {
            "json" => "application/json",
            "xml" => "application/xml",
            _ => "text/csv"
        };
        string name = format.ToLowerInvariant() switch
        {
            "json" => "shipments.json",
            "xml" => "shipments.xml",
            _ => "shipments.csv"
        };
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{name}\"");
        return Content(body, type, Encoding.UTF8);
    }
}
