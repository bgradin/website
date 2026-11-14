# Specification: SQLite Trunk

## 1. Objective

Implement an alternative SQLite trunk with slightly different storage semantics.

File storage must follow the current convention used by SqliteBsonTrunk.

Use SQLite as the underlying persistence engine, with a relational representation of tree structure and independently addressable nodes.

The storage engine must support JSON Pointer (RFC 6901) as its primary mechanism for addressing individual nodes.

Primary objectives:

- Efficient random access to individual nodes.
- Efficient insertion, deletion, modification, and movement of nodes.
- Support for arbitrarily large trees within SQLite's practical storage limits.
- Support for dynamically typed JSON-compatible values.
- Deterministic node addressing using JSON Pointer.
- Minimal dependencies and operational complexity.
- A clean .NET API that abstracts away SQLite implementation details.

## 2. Technology Requirements

- Use SQLite as the embedded database.
- Use SQLite JSONB for persisted primitive values where appropriate.
- Store database files directly on the filesystem.

Reference standard:

RFC 6901: JSON Pointer
https://www.rfc-editor.org/rfc/rfc6901

Implement RFC 6901 semantics rather than inventing a custom path syntax.

## 3. Data Model

Represent the JSON structure as a tree of independently addressable nodes.

Supported JSON types:

| Type    | Description                       |
| ------- | --------------------------------- |
| Object  | Collection of named child nodes   |
| Array   | Ordered collection of child nodes |
| String  | String primitive                  |
| Number  | Numeric primitive                 |
| Boolean | Boolean primitive                 |
| Null    | Null value                        |

Every node must have a unique, persistent identifier.

Objects and arrays must be represented as containers whose children are independently stored records.

Primitive values must be stored as SQLite JSONB values where appropriate.

### 3.1 Node structure

Each node must contain:

- `Id`: Unique, persistent identifier.
- `ParentId`: Parent node identifier; null for the root.
- `Name`: Object property name, null for array elements and root.
- `Position`: Array element position, null for object properties and root.
- `Type`: Node type discriminator.
- `Value`: JSONB payload for primitive values; null for containers.

This node structure is internal to the SQLite trunk implementation. `ParentId` records the persisted parent-child relationship and must not be exposed through the public trunk API or returned JSON values. Consumers must not need to know about or manage parent references to use the trunk.

### 3.2 Suggested SQLite schema

```sql
CREATE TABLE Nodes (
    Id       INTEGER PRIMARY KEY,
    ParentId INTEGER REFERENCES Nodes(Id),
    Name     TEXT,
    Position INTEGER,
    Type     INTEGER NOT NULL,
    Value    BLOB,

    FOREIGN KEY (ParentId) REFERENCES Nodes(Id)
);

CREATE UNIQUE INDEX IX_Nodes_ObjectProperty
    ON Nodes(TreeId, ParentId, Name)
    WHERE Name IS NOT NULL;

CREATE UNIQUE INDEX IX_Nodes_ArrayPosition
    ON Nodes(TreeId, ParentId, Position)
    WHERE Position IS NOT NULL;

CREATE INDEX IX_Nodes_ParentId
    ON Nodes(TreeId, ParentId);
```

The implementation may adjust this schema to enforce the required invariants more effectively.

Consider whether a composite foreign key or equivalent validation is necessary to ensure parent and child nodes belong to the same tree.

### 3.3 Structural invariants

- Exactly one tree per database.
- Every tree has exactly one root node.
- Root nodes have no parent, name, or position.
- Object children have a non-null name and null position.
- Array children have a non-null position and null name.
- Primitive nodes cannot have children.
- Container nodes cannot have primitive values.
- Array positions must be contiguous and zero-based after completed mutations.
- Object property names must be unique within their parent.
- Nodes cannot reference parents belonging to another tree.
- Cycles are prohibited.

## 4. Trunk interface

The storage class must implement the ITrunk interface.

### 4.1 GET semantics

- Resolve the pointer to exactly one node.
- Return the corresponding JSON value.
- Return the complete subtree when the matched node is an object or array.
- Return null when the pointer does not resolve to a node, or resolves to an existing value of null.

An empty JSON Pointer (`""`) identifies the root node.

### 4.2 PUT semantics

- Resolve the pointer to exactly one location.
- Replace the existing node's value.
- Preserve the existing node ID when replacing an existing node.
- If replacing a container with a primitive, remove all descendants.
- If replacing a primitive with a container, create the appropriate child nodes.
- If replacing one container with another, rebuild its descendants while preserving the container's node ID.
- Execute the operation transactionally.

For a missing destination, define behavior explicitly:

- Intermediate parent nodes must already exist.
- A missing final object property may be created.
- Array insertion must follow explicitly defined array semantics.
- Do not silently create missing intermediate containers.

For RFC 6901 compatibility, distinguish pointer resolution from the application-specific creation semantics of PUT.

### 4.3 DELETE semantics

- Resolve the pointer to exactly one node.
- Delete the matched node and all descendants.
- Return false when the pointer does not resolve.
- Return true when a node is deleted.
- Execute the operation transactionally.
- Prevent deletion of the root through this method. Root deletion should use `DeleteTree`.

When deleting an array element, reindex subsequent elements so positions remain contiguous and zero-based.

### 4.4 MOVE semantics

`Move(treeId, sourcePointer, destinationPointer)`:

- Resolve the source node.
- Resolve the destination parent and destination property/index.
- Relocate the source subtree without reconstructing or reserializing it.
- Preserve the IDs of all nodes in the moved subtree.
- Prevent moving a node beneath itself or one of its descendants.
- Update parent, name, or position as appropriate.
- Reindex affected arrays.
- Execute the operation transactionally.

Clearly document the destination semantics and how moving into an existing object property or array index is handled.

## 5. JSON Pointer Implementation

Implement RFC 6901.

Reference:
https://www.rfc-editor.org/rfc/rfc6901

### 5.1 Supported syntax

A JSON Pointer consists of slash-separated reference tokens.

Examples:

| Pointer            | Meaning                     |
| ------------------ | --------------------------- |
| `""`               | Root node                   |
| `/name`            | Root object's name property |
| `/metadata/tags/0` | First tag                   |
| `/items/0/name`    | Name of the first item      |
| `/items/12`        | Thirteenth array element    |
| `/a~1b`            | Property named `a/b`        |
| `/m~0n`            | Property named `m~n`        |

### 5.2 Token decoding

Implement RFC 6901 escaping:

- `~1` represents `/`.
- `~0` represents `~`.

Decode tokens in the correct order to avoid incorrectly interpreting escaped sequences.

Pointer parsing must correctly handle:

- Empty property names.
- Property names containing `/`.
- Property names containing `~`.
- Unicode.
- Empty pointers.
- Invalid escape sequences.

JSON Pointer URI fragment representation (`#...`) is optional unless explicitly required by the application. If supported, implement the RFC's URI fragment decoding rules.

### 5.3 Object traversal

When the current node is an object:

- Interpret the reference token as a property name.
- Use the `(ParentId, Name)` index to retrieve the child.
- Property matching must be case-sensitive.
- An empty token must correctly address an empty property name.

### 5.4 Array traversal

When the current node is an array:

- Interpret the reference token as an array index.
- Validate that the token follows JSON Pointer array-index syntax.
- Reject invalid indices, negative indices, and out-of-range indices.
- Use the `(ParentId, Position)` index to retrieve the element.

The `-` token has special meaning only in the context of JSON Patch array insertion. It must not be treated as an existing array element by ordinary JSON Pointer GET operations.

## 6. JSON Serialization and SQLite JSONB

Use `System.Text.Json.Nodes` as the public JSON representation.

SQLite JSONB is SQLite's own binary format, not PostgreSQL JSONB.

Requirements:

- Convert JSON text to SQLite JSONB using `jsonb()`.
- Convert JSONB to JSON text using `json()`.
- Deserialize through `System.Text.Json`.
- Do not store entire trees as single JSON documents.
- Do not store containers as JSONB when their children are independently represented as nodes.
- Preserve JSON types and numeric semantics.
- Document numeric precision limitations.

Benchmark storing primitive values in JSONB against typed SQLite columns.

Select the approach that provides the best balance of simplicity, correctness, and performance.

## 7. Performance Requirements

The primary objective is to avoid reading or rewriting the entire tree when accessing or modifying an individual node.

Expected complexity:

| Operation                      | Expected complexity                  |
| ------------------------------ | ------------------------------------ |
| Retrieve node by ID            | O(log n)                             |
| Resolve one JSON Pointer token | O(log n)                             |
| Resolve pointer of depth d     | O(d log n)                           |
| Update primitive value         | O(log n)                             |
| Add object property            | O(log n)                             |
| Add array element at end       | O(log n)                             |
| Delete leaf                    | O(log n)                             |
| Retrieve subtree               | O(log n + k)                         |
| Move subtree                   | O(log n), excluding array reindexing |
| Delete subtree                 | O(log n + k)                         |

Where:

- `n` is the total number of nodes.
- `d` is the number of reference tokens in the JSON Pointer.
- `k` is the number of nodes in a returned or deleted subtree.

Use indexed queries for every pointer traversal step.

Do not materialize the entire tree to resolve a JSON Pointer.

Array insertion and deletion may require O(k) reindexing.

## 8. Transactions and Data Integrity

All multi-record mutations must be transactional.

Examples:

- Creating a tree.
- Adding a subtree.
- Replacing a container.
- Deleting a subtree.
- Moving a subtree.
- Inserting or deleting array elements.
- Reindexing array positions.

Enable SQLite foreign key enforcement.

Consider WAL mode for improved reader/writer concurrency.

Prevent:

- Moving a node beneath itself.
- Moving a node beneath one of its descendants.
- Creating invalid parent-child relationships.
- Adding children to primitive nodes.
- Creating duplicate object properties.
- Creating multiple roots for one tree.
- Deleting a root through ordinary DELETE operations.

A failed mutation must leave the database in its original valid state.

## 9. Persistence and Lifecycle

Provide:

- Database creation and initialization.
- Schema versioning and migrations.
- Configurable database file location.
- Safe connection lifecycle management.
- Graceful disposal.
- Appropriate handling of database corruption and I/O failures.

Data must survive application restarts.

## 10. Testing Requirements

### 10.1 Basic storage operations

Test:

- Creating and reopening trees.
- Creating objects and arrays.
- Inserting primitive values.
- Retrieving nodes by pointer.
- Updating primitive values.
- Replacing nodes with different JSON types.
- Deleting nodes.
- Deleting subtrees.
- Moving nodes.
- Deeply nested structures.

### 10.2 JSON Pointer compatibility

Test:

- Empty pointer/root selection.
- Direct property access.
- Nested property access.
- Array indexing.
- Empty property names.
- Escaped `/` characters.
- Escaped `~` characters.
- Properties containing both `/` and `~`.
- Unicode property names
