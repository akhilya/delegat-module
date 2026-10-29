using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services.Marketplace;
using Delegat.Services.ScheduledTasks;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
public sealed class ScheduledTasksController : ControllerBase
{
    private readonly DataContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEnumerable<IScheduledTaskAction> _actions;
    private readonly IMarketplaceAdapterRegistry _adapters;

    public ScheduledTasksController(
        DataContext db,
        ICurrentUserService currentUser,
        IEnumerable<IScheduledTaskAction> actions,
        IMarketplaceAdapterRegistry adapters)
    {
        _db = db;
        _currentUser = currentUser;
        _actions = actions;
        _adapters = adapters;
    }

    [HttpGet("/scheduled-tasks/actions")]
    public IActionResult Actions() => Ok(_actions
        .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            id = group.Key,
            name = group.First().DisplayName,
            description = group.First().Description,
        })
        .OrderBy(x => x.name));

    [HttpGet("/scheduled-tasks/connections")]
    public async Task<IActionResult> ActionConnections([FromQuery] string actionId, CancellationToken ct)
    {
        MarketplaceCapabilities requiredCapability;
        try { requiredCapability = MarketplaceActionExecutor.RequiredCapability(actionId); }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Unknown scheduled action." });
        }

        var storeId = await RequireStoreIdAsync(ct);
        var connections = await _db.marketplaceConnections.AsNoTracking()
            .Where(x => x.storeId == storeId && x.isActive)
            .OrderBy(x => x.name)
            .Select(x => new { x.Id, x.name, x.adapterKey })
            .ToListAsync(ct);

        return Ok(connections
            .Where(x => _adapters.GetByKey(x.adapterKey)?.Capabilities.HasFlag(requiredCapability) == true)
            .Select(x => new { id = x.Id, name = x.name, adapterKey = x.adapterKey }));
    }

    [HttpGet("/scheduled-tasks")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync(ct);
        var tasks = await _db.scheduledTasks.AsNoTracking()
            .Where(x => x.storeId == storeId)
            .OrderBy(x => x.createdAt)
            .ToListAsync(ct);
        return Ok(tasks.Select(ToDto));
    }

    [HttpPost("/scheduled-tasks")]
    public async Task<IActionResult> Create([FromBody] ScheduledTaskInput input, CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync(ct);
        var userId = _currentUser.GetUserId();
        if (userId == null)
            return Unauthorized();
        if (!ScheduledTaskInputValidator.IsValid(input, out var error))
            return BadRequest(new { error });

        var now = DateTime.UtcNow;
        var task = new ScheduledTask
        {
            storeId = storeId,
            authorUserId = userId.Value,
            name = input.Name.Trim(),
            actionId = input.ActionId.Trim(),
            parametersJson = JsonSerializer.Serialize(input.Parameters ?? new()),
            intervalMinutes = input.IntervalMinutes,
            timeZoneId = input.TimeZoneId.Trim(),
            isEnabled = input.IsEnabled,
            nextRunAt = now.AddMinutes(input.IntervalMinutes),
            createdAt = now,
            updatedAt = now,
        };
        _db.scheduledTasks.Add(task);
        await _db.SaveChangesAsync(ct);
        return Created($"/scheduled-tasks/{task.Id}", ToDto(task));
    }

    [HttpPut("/scheduled-tasks/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ScheduledTaskInput input, CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync(ct);
        if (!ScheduledTaskInputValidator.IsValid(input, out var error))
            return BadRequest(new { error });
        var task = await _db.scheduledTasks.FirstOrDefaultAsync(x => x.Id == id && x.storeId == storeId, ct);
        if (task == null)
            return NotFound();

        task.name = input.Name.Trim();
        task.actionId = input.ActionId.Trim();
        task.parametersJson = JsonSerializer.Serialize(input.Parameters ?? new());
        task.intervalMinutes = input.IntervalMinutes;
        task.timeZoneId = input.TimeZoneId.Trim();
        task.isEnabled = input.IsEnabled;
        task.nextRunAt = DateTime.UtcNow.AddMinutes(input.IntervalMinutes);
        task.updatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(task));
    }

    [HttpDelete("/scheduled-tasks/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync(ct);
        var task = await _db.scheduledTasks.FirstOrDefaultAsync(x => x.Id == id && x.storeId == storeId, ct);
        if (task == null)
            return NotFound();
        _db.scheduledTasks.Remove(task);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("/scheduled-tasks/{id:int}/runs")]
    public async Task<IActionResult> Runs(int id, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var storeId = await RequireStoreIdAsync(ct);
        var exists = await _db.scheduledTasks.AsNoTracking().AnyAsync(x => x.Id == id && x.storeId == storeId, ct);
        if (!exists)
            return NotFound();
        var runs = await _db.scheduledTaskRuns.AsNoTracking()
            .Where(x => x.scheduledTaskId == id && x.storeId == storeId)
            .OrderByDescending(x => x.startedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);
        return Ok(runs);
    }

    private async Task<int> RequireStoreIdAsync(CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("No store selected", code: "store_required");
        return storeId.Value;
    }

    private static object ToDto(ScheduledTask task) => new
    {
        id = task.Id,
        storeId = task.storeId,
        name = task.name,
        actionId = task.actionId,
        parameters = JsonSerializer.Deserialize<JsonElement>(task.parametersJson),
        intervalMinutes = task.intervalMinutes,
        timeZoneId = task.timeZoneId,
        isEnabled = task.isEnabled,
        nextRunAt = task.nextRunAt,
        createdAt = task.createdAt,
        updatedAt = task.updatedAt,
    };
}

public sealed class ScheduledTaskInput
{
    public string Name { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Parameters { get; set; }
    public int IntervalMinutes { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public bool IsEnabled { get; set; } = true;
}
