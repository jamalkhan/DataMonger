# Relationships (`children`)

A **relationship** nests rows of another source inside a record: a customer's orders, an order's lines, a line's
product. Relationships are declared in an output's `children` array (or in the `children` of a child, to any
depth, or of an [object group](Outputs.md#object-groups)).

```jsonc
"children": [
  { "source": "orders", "as": "orders", "cardinality": "many",
    "on": { "parent": "customer_id", "child": "customer_id" },
    "fields": [ { "name": "order_id", "from": "order_id", "type": "integer" } ],
    "children": [
      { "source": "order_lines", "as": "lines", "on": { "parent": "order_id", "child": "order_id" },
        "fields": [ { "name": "sku", "from": "sku" } ] }
    ] }
]
```

Each child is itself a node: its `fields` read **its own source's** columns, its `children` join against **its**
rows, and so on. Runnable examples: [02-nested-children](samples/02-nested-children/nested.etlconfig),
[05-keyed-relations](samples/05-keyed-relations/keyed-relations.etlconfig),
[06-merge-by](samples/06-merge-by/merge-by.etlconfig), [13-joins-advanced](samples/13-joins-advanced/joins-advanced.etlconfig).

## Property reference

| Property | Type | Required | Default | Section |
| --- | --- | --- | --- | --- |
| [`source`](#source) | string | Yes | none | Alias of the child source |
| [`as`](#as) | string | Yes | none | JSON property name |
| [`cardinality`](#cardinality) | `one` \| `many` | No | `many` | Object or array |
| [`on`](#on) | object | Yes | none | Join keys (single or composite) |
| [`caseInsensitiveJoin`](#caseinsensitivejoin) | bool | No | `false` | Ignore case when matching |
| `fields` | array of field | No | `[]` | The child's fields ([Outputs.md#fields](Outputs.md#fields)) |
| `children` | array of child | No | `[]` | Nested relationships |
| [`filter`](#filter) | array | No | none | Keep only matching child rows |
| [`distinctBy`](#distinctby) | string | No | none | First child per distinct column value |
| [`limit`](#limit) | integer | No | none | Cap the number of children |
| [`shareDistinct`](#sharedistinct) | bool | No | `false` | Share distinct set and limit between relations with one `as` |
| [`inline`](#inline) | bool | No | `false` | Splice the child's fields into the parent |
| [`keyBy`](#keyby) | object | No | none | Render as an object keyed by a computed string |
| [`keyedSingle`](#keyedsingle) | bool | No | `false` | One object per key instead of an array |
| [`keyedSingleBestOf`](#keyedsinglebestof) | string | No | none | Which row wins a keyed-single key |
| [`mergeBy`](#mergeby) | string | No | none | Merge rows with equal column value into one entry |
| [`mergePrefix`](#mergeprefix) | object | No | none | Per-row prefix for merged properties |

Output-level: [`requireRelations`](#requirerelations). A relationship can also be written as an
[inline relationship *field*](#inline-relationship-fields), which places the array at a chosen position.

---

### source

The alias of the child source. Unknown alias: `unknown source alias '<alias>'.` The same source may be used by many
relationships (each reads it separately; under `hashInMemory` each relationship builds its own in-memory index).
A source may also be both a primary and a child of the same output (see `order_events` in
[10-filter-distinct-require](samples/10-filter-distinct-require/filters.etlconfig)).

### as

The JSON property name under which the nested value is written (required: `a relationship on source '<s>' is
missing 'as'.`). **Several relationships may use the same `as`**; see
[Sharing one array between several sources](#sharing-one-array-between-several-sources).

### cardinality

| Value | Rendered as | When nothing matches |
| --- | --- | --- |
| `many` (default) | An array of objects | Property **omitted** (never `[]`) |
| `one` | A single object (the first matching child) | Property omitted |

Remarks:

* `one` keeps the **first** matching child *before* the relationship's `filter` is applied. If that first child
  fails the filter, nothing is written even though a later child would have passed. Put the condition in
  the join keys (or pre-filter the file) if you need "the first *matching* row".
  (`customer 1001`'s first email row is `active`; with `cardinality: "one"` and an `inactive` filter nothing is
  written for them although a third row is inactive.)
* "First" is the first in file order under `hashInMemory`; under `externalMerge` it is the first in sorted order,
  which for equal keys is not guaranteed to be file order ([RunOptions.md](RunOptions.md#joinstrategy)).
* Rows without a parent (orphans) are ignored; parents without children are still written.
* If several relationships share an `as`, the result is always an array, whatever their cardinalities.
* A child with an empty `fields` list renders `{}` per child, which is handy for existence checks
  ([`requireRelations`](#requirerelations)) but rarely wanted in a feed.

### on

The join keys linking a child to its **parent node**.

```jsonc
"on": { "parent": "customer_id", "child": "customer_id" }                       // one column each side
"on": { "parents": [ "customer_id", "brand" ], "children": [ "customer_id", "brand" ] }   // composite
```

| Property | Type | Meaning |
| --- | --- | --- |
| `parent` | string | Column on the parent node's source |
| `child` | string | Column on the child's source |
| `parents` | array of string | Composite key: parent columns (takes precedence over `parent`) |
| `children` | array of string | Composite key: child columns, matched **by position** with `parents` |

Remarks:

* The parent is the node the relationship is declared on: the primary source for a top-level child, the *child*
  source for a nested one (so the join column of a grandchild must exist in its parent child source), and the
  enclosing node for a relationship inside an object group.
* Matching compares **raw text exactly** (ordinal, case-sensitive): `1001` does not match `01001` and
  `Ada@x.com` does not match `ada@x.com` (unless [`caseInsensitiveJoin`](#caseinsensitivejoin)). Source `fields`
  typing does not affect joins.
* A **blank key matches blank keys**: a parent with an empty join cell is joined to every child whose join cell is
  also empty. Filter such rows out if that is not wanted.
* Composite: every column must match. Both lists must have the same length. Works with both join strategies
  ([RunOptions.md](RunOptions.md#joinstrategy)); under `externalMerge` all direct children of a node must use the same
  parent columns.
* Errors: `relationship '<as>' requires 'on.parent'/'on.child' (or 'on.parents'/'on.children').`,
  `join '<as>': 'on.parents' and 'on.children' must have the same number of columns.`,
  `join '<as>': parent column '<c>' not found on the parent source.`,
  `join '<as>': child column '<c>' not found in source '<alias>'.`

Sample: composite keys in [07-external-merge](samples/07-external-merge/external-merge.etlconfig) and
[13-joins-advanced](samples/13-joins-advanced/joins-advanced.etlconfig).

### caseInsensitiveJoin

Match this relationship's keys ignoring case on both sides (the values are lower-cased with the invariant
culture).

| | |
| --- | --- |
| **Type** | bool |
| **Default** | `false` |

Typical use: an email address stored in different cases in two files. It affects only this relationship, not the
sources. Under `externalMerge` every direct child of a node must agree on the setting. Lookups have no such option.
Sample: `email_match_ignoring_case` vs `email_match_exact` in
[13-joins-advanced](samples/13-joins-advanced/joins-advanced.etlconfig) (customer 1001's stored address
`Ada.Lovelace@example.com` only matches the lower-case `emails` rows with the option on).

### filter

Conditions (AND) a **child row** must satisfy to be included. Same condition shape and operators as the
output-level [`filter`](Outputs.md#filter), evaluated against the **child source's raw columns**.

```jsonc
"filter": [ { "column": "status", "op": "ne", "value": "returned" } ]
```

Remarks:

* Applied at render time, before `distinctBy` and `limit`: a row failing the filter never consumes a distinct
  slot or a limit slot.
* Columns are validated against the child source (the message says "primary source": `filter column '<c>' not found
  in the primary source.`).
* A relationship with no surviving children is omitted exactly as if nothing had matched.

### distinctBy

Keep only the **first child per distinct value** of a child-source column (exact text; blank counts as one value).

```jsonc
"distinctBy": "email_address"
```

Applied after `filter` and before `limit`. Mutually exclusive with [`mergeBy`](#mergeby)
(`'mergeBy' and 'distinctBy' are mutually exclusive.`). Unknown column: `join '<as>': distinctBy column '<c>' not found
in source '<alias>'.` The output-level [`distinctBy`](Outputs.md#distinctby) is a different feature (it
deduplicates *primary* rows).

### limit

Cap the number of children written (`"limit": 1` = "first available"). Applied after `filter` and `distinctBy`.
Under `externalMerge` "first N" are the first N in sorted order. For several relationships sharing an `as`, each has
its own limit unless [`shareDistinct`](#sharedistinct) is used.

### shareDistinct

When relationships share one `as` (and so render into one array) and set `shareDistinct: true`, they share a
**single distinct set** and a **single limit** across the whole array.

| | |
| --- | --- |
| **Type** | bool |
| **Default** | `false` |

* The distinct key is the value of each relationship's own [`distinctBy`](#distinctby) column; values are
  compared as one pool, so the same address arriving from two files is written once.
* The shared limit is the first `limit` found among the `shareDistinct` relations and counts every element written
  by them. Relationships in the array that do **not** set `shareDistinct` keep their own per-relation behavior
  and are not counted.
* A relation's `filter` is applied first, so a failing row never claims a slot.

Sample: `known_addresses` in [13-joins-advanced](samples/13-joins-advanced/joins-advanced.etlconfig) (written
with inline fields, which accept the same option).

### inline

Splice a child's fields directly into the **parent object** instead of nesting them under `as`. The classic use is
flattening a header onto its detail rows (an order's date onto each order line).

```jsonc
{ "source": "orders", "as": "order", "cardinality": "one", "inline": true,
  "on": { "parent": "order_id", "child": "order_id" },
  "fields": [ { "name": "order_id", "from": "order_id" }, { "name": "order_date", "from": "order_date", "type": "date" } ] }
// -> {"line_id":1, "sku":"TENT-2P", ..., "order_id":5001, "order_date":"2024-01-15"}
```

Rules:

* Requires `cardinality: "one"`: `join '<as>': 'inline' requires cardinality 'one' (there's no flat rendering of a
  many-relation).`
* Must be the **only** relationship with its `as`: `relationship '<as>': 'inline' must be the only source sharing
  this 'as' name`.
* The spliced fields are written **after** the parent's own fields (and after earlier relations). Their names are
  not checked against the parent's: a clash writes the property twice.
* `filter`, `distinctBy` and `limit` still apply to the (single) child; nothing is written when there is no
  surviving child.

Sample: `order_lines_flat` in [02-nested-children](samples/02-nested-children/nested.etlconfig).

---

### keyBy

Render a relationship as an **object whose property names are computed** from each child row, instead of an array:

```jsonc
{ "source": "emails", "as": "emails_by_brand", "cardinality": "many",
  "on": { "parent": "customer_id", "child": "customer_id" },
  "keyBy": { "from": "brand",
             "pipeline": [ { "op": "map", "cases": { "Acme": "ACM", "Acme Kids": "KID" } } ] },
  "fields": [ { "name": "email_address", "from": "email_address" } ] }
// -> "emails_by_brand": { "ACM": [ {...}, {...} ], "KID": [ {...} ] }
```

| Property | Type | Meaning |
| --- | --- | --- |
| `keyBy.from` | string | Column of the **child** row that starts the pipeline (typed value). Optional if the pipeline only uses `field`/`literal` operands |
| `keyBy.pipeline` | array of steps | Required. Produces the key; see [Transforms](Transforms/Readme.md) (typically `map` or `concat`) |

Remarks:

* The key is converted to a string. A child whose key comes out **empty or null is dropped**.
* A child whose pipeline **throws** (e.g. a `map` with no case and no `default`) is also dropped from the keyed
  object, and a row is written to `<fileBaseName>_<timestamp>_warnings.csv` (field = the relationship's `as`,
  reason = the message). It never skips the record or aborts the run, whatever `onError` says.
* Each key holds an **array** of the children that produced it (use [`keyedSingle`](#keyedsingle) for one object).
  `filter`, `distinctBy` and `limit` apply to the relationship's rows *before* keys are assigned.
* Keys appear in first-seen order.
* The property is **always written**, as `{}` when no child produced a key (unlike arrays, which are omitted).
* All relationships sharing an `as` must either all use `keyBy` or none:
  `relationship '<as>': all sources sharing this 'as' must either all use 'keyBy' or none.` Relations that share a
  keyed `as` put their rows under the same keys.
* `keyBy.pipeline` empty: `join '<as>': keyBy requires a 'pipeline'.` `keyBy.from` unknown:
  `join '<as>': keyBy.from column '<c>' not found in source '<alias>'.`
* An array field [`keysOf`](Outputs.md#keysof) can list a node's keyed relations' keys.

Sample: [05-keyed-relations](samples/05-keyed-relations/keyed-relations.etlconfig).

### keyedSingle

With `keyBy`: each key holds **one object** (the first matching child) instead of an array.

| | |
| --- | --- |
| **Type** | bool |
| **Default** | `false` |

Relationships sharing a keyed `as` must agree on it (`all sources sharing this 'as' must agree on 'keyedSingle'.`).
Ignored without `keyBy`.

### keyedSingleBestOf

With `keyBy` + `keyedSingle`: when several children yield the same key, keep the one with the **smallest numeric
value** of this child column, instead of the first.

| | |
| --- | --- |
| **Type** | string (a child column) |
| **Default** | none (first wins) |

* The value is parsed as an invariant-culture number; a missing or non-numeric value ranks **last** (loses to any
  number). Ties keep the first-seen row.
* Requires `keyedSingle: true` (`'keyedSingleBestOf' requires 'keyedSingle: true'.`); the column must exist
  (`keyedSingleBestOf column '<c>' not found in source '<alias>'.`).
* To prefer the *largest* value, store its negation, or order the file so the best row is first and leave this
  unset.

Sample: customer 1002 has two `Acme` phone rows (priority 2, then 1); `phones_by_brand` keeps the priority-1 row.

### mergeBy

With `keyBy` (arrays, not `keyedSingle`): instead of one array entry per row, **group rows by this column's value
and merge each group into one entry**. For example, the same email address stored once per brand becomes one
contact carrying both brands' properties.

```jsonc
{ "source": "emails", "as": "contacts", "on": { "parent": "customer_id", "child": "customer_id" },
  "keyBy": { "pipeline": [ { "op": "literal", "default": "acme-shop" } ] },
  "mergeBy": "email_address",
  "mergePrefix": { "from": "brand", "pipeline": [ { "op": "map", "cases": { "Acme": "acme", "Acme Kids": "kids" } } ] },
  "fields": [
    { "name": "contact_value", "from": "email_address" },
    { "name": "properties", "type": "object",
      "fields": [ { "name": "status", "from": "status" } ],
      "children": [ { "source": "orders", "as": "order_ids", "on": { "parent": "customer_id", "child": "customer_id" },
                      "distinctBy": "order_id", "fields": [ { "name": "order_id", "from": "order_id", "type": "integer" } ] } ] }
  ] }
```

How a merged entry is built (per key, grouping by the `mergeBy` value, across **all** relationships that share the
`as` and set `mergeBy`):

| Part | Rule |
| --- | --- |
| Plain fields (scalar, lookup, coalesce) | Written once, taken from the **first row** of the group |
| An [object group](Outputs.md#object-groups) field (e.g. `properties`) | Every contributing row's own group fields are written into the one object, each name prefixed `<prefix>_<name>` (`acme_status`, `kids_status`) |
| An array relationship inside the group (`order_ids`) | **Never prefixed.** Written once, at the position where it is first seen, as the de-duplicated union of every contributor's matches (a child row reached through several contributors appears once; each relationship's `distinctBy` then applies across the union and the first relationship's `limit` caps it). Omitted when empty |
| A non-array relationship inside the group (single/keyed/inline) | Per row, prefixed like a field |
| Other relationships on the entry | Written once, joined from the first row that has them |

* Rows from **different** relationships (different shapes, e.g. `emails` and `email_prefs`) merge into the same
  entry when their `mergeBy` values are equal; the group walks the union of their items.
* Two rows from the *same* relationship with the *same* prefix (for example two history rows for one customer and
  address in a source with no `distinctBy`) contribute only the **first** row's group fields.
* Rows from relationships that do **not** set `mergeBy` stay individual entries and are written before the merged
  ones.
* Errors: `'mergeBy' and 'distinctBy' are mutually exclusive.`; `mergeBy column '<c>' not found in source
  '<alias>'.`
* **`mergeBy` has no effect without `keyBy`** (verified): on a plain relationship it is silently ignored.

Sample: [06-merge-by](samples/06-merge-by/merge-by.etlconfig): customer 1001's `ada.lovelace@example.com` appears
in two brands (`acme_*`, `kids_*` properties) and in the preferences file (`acme_channel`, `acme_opted_in`).

### mergePrefix

How to compute each row's property prefix for a merged object group.

| Property | Type | Meaning |
| --- | --- | --- |
| `mergePrefix.from` | string | Child column that starts the pipeline (optional) |
| `mergePrefix.pipeline` | array of steps | Required when `mergePrefix` is present; the result (string) is the prefix |

* A null or empty prefix means "no prefix" for that row.
* Omitting `mergePrefix` entirely merges the fields **unprefixed**: a property name that several rows write appears
  once per row (duplicate JSON property names), so give each source a prefix when their fields overlap.
* A row whose prefix pipeline **throws** (an unmapped code with no `map` default) is dropped from the merge with a
  warning in the warnings file; the record is kept.
* Errors: `join '<as>': mergePrefix requires a 'pipeline'.`, `mergePrefix.from column '<c>' not found in source '<alias>'.`
* Ignored without `mergeBy`.

---

## Sharing one array between several sources

Relationships that declare the **same `as`** are written as **one array**, concatenated in declaration order. This
is how rows from differently shaped files land in one list:

```jsonc
{ "source": "emails", "as": "contacts", "on": { ... },
  "fields": [ { "name": "contact_type", "type": "string", "pipeline": [ { "op": "literal", "default": "email" } ] },
              { "name": "contact_value", "from": "email_address" } ] },
{ "source": "phones", "as": "contacts", "on": { ... },
  "fields": [ { "name": "contact_type", "type": "string", "pipeline": [ { "op": "literal", "default": "phone" } ] },
              { "name": "contact_value", "from": "phone_number" } ] }
```

* The array position is where the **first** relationship with that name would appear (after the node's fields, in
  declaration order), and the property is omitted when no source contributed anything.
* The result is an array even if the relationships are `cardinality: "one"`.
* Each relationship keeps its own `filter`, `distinctBy`, `limit`.
* The same rule applies to `keyBy` relationships (they share one keyed object) and is what [`mergeBy`](#mergeby)
  builds on.

## Inline relationship fields

A **field** (rather than a child) with a `source` is a relationship rendered as an **array of objects placed
exactly where the field is listed**, so it can sit between scalar fields (relationships in `children` always come
after all fields).

```jsonc
{ "name": "mobile_numbers", "type": "array",
  "source": "phones",
  "on": { "parent": "customer_id", "child": "customer_id" },
  "filter": [ { "column": "phone_type", "op": "eq", "value": "mobile" } ],
  "distinctBy": "phone_number", "limit": 1,
  "fields": [ { "name": "number", "from": "phone_number" }, { "name": "brand", "from": "brand" } ] }
```

| Property | Meaning |
| --- | --- |
| `source`, `on`, `fields`, `children` | As for a child (always cardinality `many`) |
| `caseInsensitiveJoin`, `filter`, `distinctBy`, `limit`, `shareDistinct` | As for a child |
| `type` | Use `"array"` (informational: it does not change the output) |

* Several inline fields with the **same `name`** are rendered as **one array**, concatenated in declaration order,
  positioned at the first of them. With `"shareDistinct": true` they share one distinct set and limit (see
  [shareDistinct](#sharedistinct)).
* Not available on an inline field: `inline`, `keyBy`, `mergeBy`, `cardinality` (use a child for those).
* Inline fields can themselves contain `children`.

Sample: `mobile_numbers` in [02-nested-children](samples/02-nested-children/nested.etlconfig) and
`known_addresses` in [13-joins-advanced](samples/13-joins-advanced/joins-advanced.etlconfig).

---

### requireRelations

An **output-level** setting (object mode): emit a primary record only if **at least one** of the named
relationships has content.

```jsonc
{ "name": "customers_with_orders", "source": "customers", "requireRelations": [ "orders" ], "filterExcludedFile": true,
  "children": [
    { "source": "orders", "as": "orders", "on": { "parent": "customer_id", "child": "customer_id" },
      "filter": [ { "column": "status", "op": "eq", "value": "shipped" } ],
      "fields": [ { "name": "order_id", "from": "order_id", "type": "integer" } ] }
  ] }
```

| | |
| --- | --- |
| **Type** | array of string (relationship `as` names on the primary node, or in its object groups) |
| **Default** | none |

* "Has content" means the relationship would write something: at least one child that passes its own `filter`
  (and, for a keyed relation, has a non-empty key). So the filter on the relationship doubles as the requirement.
* Excluded records are counted as filtered and, with `filterExcludedFile: true`, written to the `_filtered.csv`.
* Evaluated after the output [`filter`](Outputs.md#filter) and before [`distinctBy`](Outputs.md#distinctby), so a
  rejected row never uses up a distinct slot.
* Unknown name: `output '<name>': requireRelations references '<n>', which is not a relation on the primary node.`
  In keyedMap mode: `requireRelations is only supported in object mode.`
* Only relationships on the primary node count; to test a deeper level, add a filter or lookup (`exists`) on the
  primary instead.

Sample: `customers_with_orders` in [10-filter-distinct-require](samples/10-filter-distinct-require/filters.etlconfig)
(customers 1004 and 1005 have no shipped orders).

## Order of operations for a relationship

For one parent record, per relationship (render time unless noted):

1. **Join** (before rendering): find the child rows with the matching key (`cardinality: one` keeps the first).
2. `filter`
3. `distinctBy`
4. `limit`
5. Keyed relationships: compute keys, group, `keyedSingle`/`keyedSingleBestOf`; `mergeBy` merges groups.
6. Render each child's own fields and nested children (recursively).

## See also

[Outputs.md](Outputs.md), [RunOptions.md#joinstrategy](RunOptions.md#joinstrategy),
[Errors.md](Errors.md), [Transforms](Transforms/Readme.md)
