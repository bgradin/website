using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Gradinware.Controllers;
using Gradinware.Data;
using Gradinware.Models.Authentication;
using Gradinware.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var root = Path.Combine(Path.GetTempPath(), "dnd-tests-" + Guid.NewGuid());
Environment.SetEnvironmentVariable("DATA_DIRECTORY", root);
try
{
    using var accounts = new AccountContext();
    accounts.Database.EnsureCreated();
    accounts.Users.AddRange(new User { Id = 1, FirstName = "User", LastName = "One", Email = "one@example.com", Password = "test-only" }, new User { Id = 2, FirstName = "User", LastName = "Two", Email = "two@example.com", Password = "test-only" });
    accounts.SaveChanges();
    using var db = new DndContext();
    // Seed the deployed single-sheet schema to exercise the upgrade with real data.
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE Documents (UserId INTEGER NOT NULL PRIMARY KEY, SchemaVersion INTEGER NOT NULL,
            Revision INTEGER NOT NULL, Data TEXT NOT NULL);
        INSERT INTO Documents VALUES (1, 1, 7, {0});
        """, "{\"name\":\"Legacy\"}");
    db.EnsureSchema();
    db.EnsureSchema();
    var legacy = await db.Documents.AsNoTracking().SingleAsync();
    if (legacy.UserId != 1 || legacy.Revision != 7 || legacy.Data != "{\"name\":\"Legacy\"}" || legacy.Id.Length != 32)
        throw new Exception("Legacy sheet was not preserved");
    DndController Controller(int? id)
    {
        var http = new DefaultHttpContext();
        if (id.HasValue) http.User = SiteSession.Principal(id.Value);
        http.Request.Headers["X-Site-Action"] = "1";
        return new DndController(accounts, db) { ControllerContext = new ControllerContext { HttpContext = http } };
    }
    void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    var one = Controller(1);
    DndController.SaveRequest Payload(long revision = 0, int schema = 1) => new()
        { SchemaVersion = schema, Revision = revision, Data = JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Ada\",\"inspiration\":false}") };
    string CreatedId(IActionResult result)
    {
        Check(result is CreatedAtActionResult);
        return JsonSerializer.SerializeToElement(((CreatedAtActionResult)result).Value).GetProperty("sheetId").GetString();
    }
    JsonElement Body(IActionResult result) => JsonSerializer.SerializeToElement(((OkObjectResult)result).Value);
    var two = Controller(2);
    Check(await Controller(null).List() is UnauthorizedResult);
    Check(await Controller(null).Load(legacy.Id) is UnauthorizedResult);
    Check(await Controller(null).Create(Payload()) is UnauthorizedResult);
    Check(await Controller(null).Save(legacy.Id, Payload()) is UnauthorizedResult);
    Check(await Controller(null).Delete(legacy.Id) is UnauthorizedResult);
    Check(await Controller(999).List() is UnauthorizedResult);
    Check(await one.Load("missing") is NotFoundResult);
    Check(await one.Save("missing", Payload()) is NotFoundResult);
    Check(await one.Delete("missing") is NotFoundResult);
    Check(Body(await two.List()).GetProperty("sheets").GetArrayLength() == 0);
    var first = CreatedId(await one.Create(Payload()));
    var second = CreatedId(await one.Create(Payload()));
    var foreign = CreatedId(await two.Create(Payload()));
    Check(first != second && first != foreign);
    Check(Body(await one.List()).GetProperty("sheets").GetArrayLength() == 3);
    Check(Body(await two.List()).GetProperty("sheets").GetArrayLength() == 1);
    Check(await two.Load(first) is NotFoundResult);
    Check(await two.Save(first, Payload(1)) is NotFoundResult);
    Check(await two.Delete(first) is NotFoundResult);
    Check(await one.Save(first, Payload()) is ConflictObjectResult);
    Check(await one.Save(first, Payload(1, 2)) is BadRequestObjectResult);
    Check(await one.Create(Payload(1)) is BadRequestObjectResult);
    one.Request.Headers.Remove("X-Site-Action");
    Check(((StatusCodeResult)await one.Create(Payload())).StatusCode == 403);
    Check(((StatusCodeResult)await one.Save(first, Payload(1))).StatusCode == 403);
    Check(((StatusCodeResult)await one.Delete(first)).StatusCode == 403);
    one.Request.Headers["X-Site-Action"] = "1";
    DndController.SaveRequest DataPayload(string json, long revision = 1) => new()
        { SchemaVersion = 1, Revision = revision, Data = JsonSerializer.Deserialize<JsonElement>(json) };
    const string sheet = """
        {"name":"Ada","strength-score":"16","inspiration":false,"formulas":{
          "strength-modifier":["max","(","(",{"field":"strength-score"},"-","10",")","/","2",",","0",")"],
          "acrobatics-score":["(",{"field":"proficiencyBonus"},"*",{"field":"acrobatics-proficiency"},")","+",{"field":"dexterity-modifier"}],
          "speed":["min","(","+",".5","*","30.",",","30",")"],
          "rounded-up":["ceil","(","3.5",")"],
          "rounded-down":["floor","(","-","3.5",")"]}}
        """;
    // New sheets use the same min/max/ceil/floor formula tokens as subsequent saves.
    var formulaSheet = CreatedId(await one.Create(DataPayload(sheet, 0)));
    var createdFormulaSheet = Body(await one.Load(formulaSheet));
    Check(createdFormulaSheet.GetProperty("schemaVersion").GetInt32() == 1);
    Check(createdFormulaSheet.GetProperty("revision").GetInt64() == 1);
    Check(JsonElement.DeepEquals(createdFormulaSheet.GetProperty("data"), JsonSerializer.Deserialize<JsonElement>(sheet)));
    Check(await one.Delete(formulaSheet) is NoContentResult);
    Check(await one.Save(first, DataPayload(sheet)) is OkObjectResult);
    var loaded = JsonSerializer.SerializeToElement(((OkObjectResult)await one.Load(first)).Value);
    Check(loaded.GetProperty("revision").GetInt64() == 2);
    Check(JsonElement.DeepEquals(loaded.GetProperty("data"), JsonSerializer.Deserialize<JsonElement>(sheet)));
    foreach (var invalid in new[] {
        "{\"formulas\":[]}", "{\"formulas\":{\"speed\":[]}}",
        "{\"formulas\":{\"speed\":[1]}}", "{\"formulas\":{\"speed\":[\"alert(1)\"]}}",
        "{\"formulas\":{\"speed\":[{\"field\":false}]}}",
        "{\"formulas\":{\"speed\":[{\"field\":\"speed\",\"extra\":true}]}}",
        "{\"speed\":{}}", "{\"speed\":30}", "{\"formulas\":null}",
        JsonSerializer.Serialize(new { formulas = new { speed = new string[201] } })
    })
    {
        Check(await one.Create(DataPayload(invalid, 0)) is BadRequestObjectResult);
        Check(await one.Save(first, DataPayload(invalid, 2)) is BadRequestObjectResult);
    }
    Check(await one.Save(first, DataPayload("{}", long.MaxValue)) is BadRequestObjectResult);
    Check(await one.Save(first, DataPayload("{\"formulas\":{}}", 2)) is OkObjectResult);
    // Two independent request contexts must reject a stale update.
    using (var concurrent = new DndContext())
    {
        await concurrent.Documents.SingleAsync(d => d.Id == first);
        Check(await one.Save(first, Payload(3)) is OkObjectResult);
        var stale = new DndController(accounts, concurrent) { ControllerContext = one.ControllerContext };
        Check(await stale.Save(first, Payload(3)) is ConflictObjectResult);
    }
    Check(await one.Delete(first) is NoContentResult);
    Check(await one.Delete(first) is NotFoundResult);
    Check(await one.Load(first) is NotFoundResult);
    Check(await one.Save(first, Payload(4)) is NotFoundResult);
    Check(await one.Load(second) is OkObjectResult);
    Check(await two.Load(foreign) is OkObjectResult);
    Check(await one.Load(legacy.Id) is OkObjectResult);
    using var other = new DndContext();
    other.EnsureSchema();
    Check(await other.Documents.CountAsync() == 3);
    Check(!await other.Documents.AnyAsync(d => d.Id == first));
    Check(Body(await one.List()).GetProperty("sheets").GetArrayLength() == 2);
    Environment.SetEnvironmentVariable("DATA_DIRECTORY", Path.Combine(root, "fresh"));
    using (var fresh = new DndContext())
    {
        fresh.EnsureSchema();
        Check(await fresh.Documents.CountAsync() == 0);
        fresh.Documents.Add(new DndDocument { UserId = 1, SchemaVersion = 1, Revision = 1 });
        fresh.Documents.Add(new DndDocument { UserId = 1, SchemaVersion = 1, Revision = 1 });
        await fresh.SaveChangesAsync();
        fresh.EnsureSchema();
        Check(await fresh.Documents.CountAsync() == 2);
    }
    Console.WriteLine("Dnd API checks passed.");
}
finally { SqliteClear(); Directory.Delete(root, true); }
void SqliteClear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
