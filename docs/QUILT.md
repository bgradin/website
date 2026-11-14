# Quilt

Quilt is a way to organize structured data as a hierarchy of small, addressable pieces. The fabric metaphor gives names to the main ideas: a **quilt** is the whole collection, **patches** are its pieces, **seams** connect pieces, **circles** group people and data, and **quilters** are the identities that work with the collection. A **trunk** stores and gets the quilt.

The metaphor is only a guide to the vocabulary. In practice, Quilt data is made of JSON objects addressed by hierarchical keys.

## The vocabulary

### Patches

A patch is a flexible, addressable unit of information (more specifically, dynamically typed properties). Properties can contain or reference other patches; this lets a large body of data be independently referenced as smaller pieces.

Protocol properties use the `q:` prefix. Common properties include:

| Property      | Meaning                                                                                 |
| ------------- | --------------------------------------------------------------------------------------- |
| `q:access`    | Circle IDs allowed to `get` and `put` a patch.                                          |
| `q:circleIds` | Circle memberships on a quilter; retained on old patches for migration compatibility.   |
| `q:seam`      | A connection to another patch.                                                          |
| `q:pinned`    | A boolean property indicating whether the patch can be modified by the current quilter. |
| `q:name`      | A human-readable circle name.                                                           |

Treat `q:` names as protocol vocabulary. Avoid using them for unrelated application fields, because Quilt may interpret them specially.

### Quilts

A quilt is a collection of patches. By protocol convention, there are three root-level patches:

- `q:content` is the top-level patch holding the rest of the main quilt material (other patches).
- `q:circles` describes groups and their memberships.
- `q:quilters` describes identities and the circles they belong to.

### Motifs

Motifs are common/reusable patches. A nonempty `q:motif` value is a relative patch path: resolution starts beneath the patch containing the property, then checks each parent location up to the quilt root. The nearest matching patch wins. Missing or inaccessible targets remain unresolved as strings. Resolved motif data is copied into decorated patches when fetched; motif information is removed before writes, preserving the relationship without storing the copied information. Reads fail if motif resolution encounters a cycle or exceeds 32 nested motif references. This resolution also applies to motif properties inside nested objects and array elements.

### Quilters

A quilter is an identity that interacts with a quilt.

### Circles

A circle is a named group used to associate quilters with access declarations. On a quilter, `q:circleIds` lists membership. On a patch, use `q:access`, for example `{ "get": ["engineering"], "put": ["engineering"] }`. `get` controls visibility and `put` controls modification; a writer must also be able to read. The special circle `everyone` grants read visibility to any valid quilter. Missing identities do not receive protected access. The `lead` membership retains its administrative bypass.

Access declarations inherit through the key hierarchy. Each declaration further restricts its ancestors, so descendants can narrow access but cannot expand it. `q:access` is validated when a non-lead quilter writes a patch. A seam is only a reference: it grants no permission to its target, and inaccessible targets are omitted from the resolved view.

For compatibility, old patches with `q:circleIds` remain readable as before and that field continues to limit writes. Replace patch-level `q:circleIds` with `q:access` during migration. Quilter membership continues to use `q:circleIds`.

The reserved circle ID `lead` represents the lead circle. Lead membership is the broad administrative membership in the current protocol conventions.

### Trunks

A trunk is the storage boundary for a quilt. Identity-bearing `Get`, `Put`, `Delete`, `GetKeys`, and `Clear` calls authorize the supplied quilter ID before returning data or changing storage. The trunk determines the delimiter used between key segments; with `.` as the delimiter, `q:content.about.index` has the segments `q:content`, `about`, and `index`.

The application uses `ContentTrunk` to enforce these rules over the JSON Pointer based `SqliteTrunk`. Each complete patch key occupies one root property in the node store, preserving independent patch boundaries. See [SQLite content storage](sqlite-trunk.md) for storage details and the completed local data conversion.

The hierarchy is a naming convention over keys. A patch stored at a key is retrieved as that patch; `GetPatch` does not assemble descendant keys into its JSON object. For example, if `q:content.about` and `q:content.about.team` are stored separately, reading `q:content.about` returns the stored `about` patch, not an `about` object with `team` added. `GetMap` can expose the hierarchy of stored keys, while a patch read can include resolved motifs. Objects inside arrays can be addressed with zero-based bracket indices, such as `q:content.people[0]` or `q:content.people[0].name`. Array elements remain part of the containing patch; their addresses are traversals within that patch rather than separately stored trunk entries.

Each key segment and each circle or quilter ID is a non-empty, case-sensitive name. Names cannot contain the trunk delimiter or `[` / `]`; complete keys may contain the delimiter only between non-empty segments. Array indices use non-negative decimal integers in brackets. Exact names are unique within their namespace. Quilt and the trunk validate these rules so a name cannot introduce extra hierarchy levels or be confused with an array address.

## Common patterns

### Give each piece a stable address

Use hierarchical keys to communicate where a patch belongs. A page might live at `q:content.about.index`, while shared navigation could live at `q:content.shared.navigation`. Stable, predictable keys make it easier to link content, inspect the quilt, and replace one piece without rewriting everything around it.

### Put shared material behind a seam

Store reusable data once and reference it where needed. Relative references resolve from the patch location upward, so place shared patches at the common ancestor where they should become visible.

### Assign access at the right level

Put `q:access` on a parent when its descendants should share visibility and editability. Add a narrower declaration to a child when it should be more restricted. Descendants cannot use a declaration to broaden an ancestor's circles.

### Keep protocol metadata separate in meaning

Use `q:` properties for Quilt concepts and ordinary names for application data. A property called `q:seam` is a relationship; it should not also be used to store an unrelated string. This convention helps readers distinguish the fabric structure from the content carried by each patch.

### Think of reads as views

A patch read can include resolved motifs and a `q:pinned` hint. `q:pinned` is derived from the current quilter's write authorization, is overwritten on reads, and is removed from data before persistence. It is presentation metadata, not an authorization declaration. The returned patch may therefore differ from its stored form; descendants stored under other keys are not folded into it. `GetMap` provides a separate view of the stored key hierarchy.

Authorization is enforced by the trunk for identity-bearing `Get`, `Put`, `Delete`, `GetKeys`, and `Clear` operations. `Quilt` forwards the authenticated quilter ID and prepares patch reads; it is not the authorization boundary. Key listings include only visible patches, deletes require write access to the target and its descendants, and clear/import requires lead membership. An empty trunk permits its first quilter record to be initialized; after initialization, clear/import requires lead membership. Trunk mutations coordinate permission checks and storage changes in a transaction.

## Recommendations

These points describe places where the protocol's conventions need clarification or stronger guarantees.

1. **Specify replacement and collision behavior.** Explain what should happen when a patch is replaced while descendants already exist, or when a new descendant is written beneath a key that already stores a whole patch. Define how property names are interpreted during collision checks.
2. **Reserve protocol fields consistently.** State whether all `q:` names are reserved in every patch and validate their types and allowed locations. `q:pinned` is ignored on writes and derived on reads.
3. **Complete the lifecycle vocabulary.** The protocol would benefit from clear conventions for managing circle membership, recording changes, and identifying the quilt's schema version.
