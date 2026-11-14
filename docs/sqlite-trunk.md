# SQLite content storage

Content now persists through `SqliteTrunk`; the BSON-backed trunk and its EF
key/value context have been removed. Database files remain under
`$DATA_DIRECTORY/sqlite/`, defaulting to `/data/sqlite/`.

## Application integration

`ContentTrunk` is the application's `IContentTrunk` implementation. It applies
Quilt's existing identity, circle, inherited access, collision, and computed
`q:pinned` rules, then stores authorized changes through `SqliteTrunk`.
Application keys and identities retain their existing meaning and `.` delimiter.
Controllers, motifs, and existing content URLs continue to use the same API.

The node store has one object root. Each separately stored Quilt patch is a
root property named by its **complete** Quilt key. This preserves the distinction
between separately stored patches and nested objects inside another patch.
For example, the separate patches `q:content.about` and
`q:content.about.team` occupy `/q:content.about` and
`/q:content.about.team`; they are not merged. A field inside the first patch is
addressed by `/q:content.about/title`. Root property names use normal JSON Pointer
escaping for `/` and `~`.

Objects, arrays, and their values are independent node records. Only primitive
values use SQLite `jsonb()` BLOBs, decoded through `json()`. Pointer traversal
uses parent/name and parent/position indexes. `GetPropertyNames` enumerates
patch names without loading their JSON. New writes never serialize BSON.
The BSON package dependency has been removed.

The low-level `SqliteTrunk` public API uses `JsonNode`, JSON Pointers, and a tree
ID (defaulting to the database filename). It does not authorize callers; do not
inject it directly into controllers in place of `IContentTrunk`.

## Completed local data conversion

The local `data/sqlite/content.db` has been converted from its five legacy BSON
records into the node store (48 nodes). Each decoded value was read back and
compared before committing. All five complete patch keys were preserved;
SQLite integrity and foreign key checks passed. The account database was verified
unchanged.

The original content database is retained as a standalone SQLite backup at
`data/sqlite/content.db.bson-backup-94a0817255ab4ae78099b232e376a5f1.db`.

This was a one-time operation on the actual local data. The application contains
no BSON importer, migration command, or automatic legacy data migration. Deploy
the converted database with the updated application. Databases and backups under
`data/` are ignored by Git. Account schema migrations are separate and unchanged.

## Rendering HTML content

Page patches can use standard lowercase HTML tags in `ui:type`, for example
`{"ui:type":"main","children":[{"ui:type":"h1","children":"Hello!"}]}`.
The renderer checks the custom component registry first (`Page`, `ReactRoot`,
`Link`, `Meta`, `Script`, and `Title`), then accepts tag names matching `^[a-z][a-z0-9-]*$`.
HTML tags and custom elements such as `my-widget` do not require individual
component implementations. This naming check accepts unknown lowercase names,
including typos; unregistered uppercase component names and malformed tag names
are rejected.

Native elements accept React props such as `className`, `htmlFor`, `aria-label`,
`data-*`, and object-valued `style`. Children and motif-backed attributes are
resolved against the page patch. Properties starting with `q:` or `ui:` are
metadata and are excluded from component and DOM props. The same rendering
rules apply during SSR and client hydration.

## Pointer and lifecycle semantics

The empty pointer selects the root; `/` selects an empty object property. Names
are case sensitive. Invalid escapes or non-pointer syntax throw
`ArgumentException`; URI fragments are unsupported. GET returns null for both a
missing location and JSON null. Array tokens must be non-negative decimal ASCII
indices without leading zeros.

PUT replaces an existing node while preserving its ID. It creates missing final
object properties, but requires existing intermediate parents. For arrays it
replaces an existing index or appends at `count` or `-`, without filling gaps.
DELETE removes a subtree and closes array gaps; it cannot delete the root.
MOVE preserves source subtree IDs, replaces an existing object destination, or
inserts before an array destination index. Destination indices are interpreted
after source removal. Root moves and moving beneath the source are rejected.
`DeleteTree` removes the root as well; PUT at the empty pointer recreates it.

Writes use transactions and savepoints, including inside caller-owned
transactions. Foreign keys, unique indexes, parent/cycle checks, WAL, and schema
versioning enforce persistence invariants. Dispose instances to close connections
and roll back uncommitted work. Use separate instances for concurrent callers;
an individual instance is not thread safe. Corruption and I/O errors propagate
instead of silently resetting the database.

JSON numeric literals are stored through JSON text and JSONB without coercion
to SQLite REAL or CLR double. Precision already lost in caller-provided floating
point values cannot be restored. SQL arithmetic/extraction and Newtonsoft
conversion have their respective numeric limits. Deep results are subject to
.NET memory, collection, and JSON serialization depth limits.

## Validation

```sh
dotnet test tests/Quilt.Authorization.Tests/Quilt.Authorization.Tests.csproj
dotnet run --project tests/ContentStorage/ContentStorage.csproj
dotnet run --project tests/SqlitePrimitives/SqlitePrimitives.csproj --configuration Release
```

Tests cover authorization, motifs, persistence, pointer escaping, array edits,
ID-preserving moves, rollback, schema rejection, empty databases, and repeated opens.

The primitive benchmark compares JSONB with typed string/boolean columns and
TEXT for exact numeric literals. One local Windows .NET 10 run with 20,000 writes
in one transaction and 20,000 indexed reads measured:

| Format | Writes | Reads including JSON parsing | Database bytes |
| --- | ---: | ---: | ---: |
| JSONB | 86.2 ms | 1,142.1 ms | 364,544 |
| Typed columns | 27.4 ms | 1,060.2 ms | 397,312 |

These are local timings without statistical confidence. JSONB retains a uniform
primitive representation and a smaller measured file; typed columns were faster
in this run but require separate conversion paths.
