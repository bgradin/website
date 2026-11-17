using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Controllers;

[ApiController]
[Route("api/dnd/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DndController(AccountContext accounts, DndContext db) : ControllerBase
{
    private int? UserId()
    {
        var id = SiteSession.UserId(HttpContext);
        return id.HasValue && accounts.Users.Any(u => u.Id == id.Value) ? id : null;
    }

    private static object Sheet(DndDocument document) => new
    {
        sheetId = document.Id, schemaVersion = document.SchemaVersion,
        revision = document.Revision, data = JsonSerializer.Deserialize<JsonElement>(document.Data)
    };

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var id = UserId();
        if (!id.HasValue) return Unauthorized();
        var documents = await db.Documents.AsNoTracking().Where(d => d.UserId == id.Value)
            .OrderBy(d => d.Id).ToListAsync();
        return Ok(new { sheets = documents.Select(Sheet) });
    }

    [HttpGet("{sheetId}")]
    public async Task<IActionResult> Load(string sheetId)
    {
        var id = UserId();
        if (!id.HasValue) return Unauthorized();
        var document = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(d => d.UserId == id.Value && d.Id == sheetId);
        return document == null ? NotFound() : Ok(Sheet(document));
    }

    public sealed class SaveRequest
    {
        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public JsonElement Data { get; set; }
    }

    private static bool ValidFieldName(string name) => name.Length is > 0 and <= 200;

    private static bool ValidFormulaToken(JsonElement token)
    {
        if (token.ValueKind == JsonValueKind.String)
        {
            var value = token.GetString();
            // Keep the token vocabulary aligned with the client formula evaluator.
            return value is "+" or "-" or "*" or "/" or "(" or ")" or "," or "min" or "max" or "ceil" or "floor"
                || Regex.IsMatch(value, @"\A(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z");
        }
        return token.ValueKind == JsonValueKind.Object
            && token.EnumerateObject().Count() == 1
            && token.TryGetProperty("field", out var field)
            && field.ValueKind == JsonValueKind.String && ValidFieldName(field.GetString());
    }

    private static bool SupportedSchema(int version) => version is 1 or 2;

    private static bool ValidAttacks(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !ValidFieldName(id.GetString()) || !ids.Add(id.GetString())
                || !entry.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.String
                || collection.GetString() is not ("weapons" or "spells")
                || !entry.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString())) return false;
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in entry.EnumerateObject())
            {
                if (!properties.Add(property.Name)) return false;
                if (property.Name is "id" or "collection" or "name") continue;
                if (property.Name == "legacy" && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) continue;
                if (property.Name is "damage" or "bonus" && property.Value.ValueKind == JsonValueKind.String) continue;
                return false;
            }
        }
        return true;
    }

    private static bool ValidData(JsonElement data, int schemaVersion) => data.ValueKind == JsonValueKind.Object
        && data.EnumerateObject().All(property => ValidFieldName(property.Name)
            && (property.Name == "attacksSpellcasting" && schemaVersion == 2
                ? ValidAttacks(property.Value)
                : property.Name == "formulas"
                ? property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.EnumerateObject().All(formula => ValidFieldName(formula.Name)
                        && formula.Value.ValueKind == JsonValueKind.Array
                        && formula.Value.GetArrayLength() is > 0 and <= 200
                        && formula.Value.EnumerateArray().All(ValidFormulaToken))
                : property.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False));

    [HttpPost]
    [RequestSizeLimit(1048576)]
    public async Task<IActionResult> Create(SaveRequest request)
    {
        var id = UserId();
        if (!id.HasValue) return Unauthorized();
        if (Request.Headers["X-Site-Action"] != "1") return StatusCode(403);
        if (!SupportedSchema(request.SchemaVersion) || request.Revision != 0 || !ValidData(request.Data, request.SchemaVersion))
            return BadRequest(new { error = "New sheets require schema version 1 or 2, revision 0, and valid character sheet data." });
        var document = new DndDocument
        {
            UserId = id.Value, SchemaVersion = request.SchemaVersion,
            Revision = 1, Data = request.Data.GetRawText()
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Load), new { sheetId = document.Id }, Sheet(document));
    }

    [HttpPut("{sheetId}")]
    [RequestSizeLimit(1048576)]
    public async Task<IActionResult> Save(string sheetId, SaveRequest request)
    {
        var id = UserId();
        if (!id.HasValue) return Unauthorized();
        if (Request.Headers["X-Site-Action"] != "1") return StatusCode(403);
        if (!SupportedSchema(request.SchemaVersion) || request.Revision < 0 || request.Revision == long.MaxValue || !ValidData(request.Data, request.SchemaVersion))
            return BadRequest(new { error = "Unsupported schema or invalid character sheet data." });
        var document = await db.Documents.SingleOrDefaultAsync(d => d.UserId == id.Value && d.Id == sheetId);
        if (document == null) return NotFound();
        if (document.Revision != request.Revision)
            return Conflict(new { error = "The sheet changed elsewhere. Reload before saving." });
        // Older clients must not replace a migrated list with their version 1 fields.
        if (request.SchemaVersion < document.SchemaVersion)
            return Conflict(new { error = "This sheet requires a newer application version. Reload before saving." });
        document.SchemaVersion = request.SchemaVersion;
        document.Data = request.Data.GetRawText();
        document.Revision = checked(request.Revision + 1);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { error = "The sheet changed elsewhere. Reload before saving." }); }
        return Ok(Sheet(document));
    }

    [HttpDelete("{sheetId}")]
    public async Task<IActionResult> Delete(string sheetId)
    {
        var id = UserId();
        if (!id.HasValue) return Unauthorized();
        if (Request.Headers["X-Site-Action"] != "1") return StatusCode(403);
        var deleted = await db.Documents.Where(d => d.UserId == id.Value && d.Id == sheetId).ExecuteDeleteAsync();
        if (deleted == 0) return NotFound();
        return NoContent();
    }
}
