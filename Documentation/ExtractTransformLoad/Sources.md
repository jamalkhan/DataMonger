# Sources

The `sources` object is a pool of **named files**. Each key is an *alias* that outputs, relationships and
lookups use to refer to the file; each value says where the file is and how to read it. Today the engine reads
**delimited text** files (CSV, TSV, pipe-delimited, ...), optionally gzip-compressed and/or PGP-encrypted.

```jsonc
"sources": {
  "customers": {
    "file": "../data/customers.csv",
    "format": "delimited",
    "delimiter": "csv",
    "quote": "\"",
    "hasHeader": true,
    "fields": {
      "customer_id": { "type": "integer" },
      "birth_date":  { "type": "date", "format": "yyyy-MM-dd" }
    }
  }
}
```

| Property | Type | Required | Default |
| --- | --- | --- | --- |
| [`file`](#file) | string | Yes | none |
| [`format`](#format) | string | No | `"delimited"` |
| [`delimiter`](#delimiter) | string | No | `","` |
| [`quote`](#quote) | string | No | `"\""` |
| [`hasHeader`](#hasheader) | bool | No | `true` (not currently honored) |
| [`fields`](#fields) | object | No | `{}` |
| [`decrypt`](#decrypt) | object | No | none |

An alias is just a label. The same alias may be used as the primary of one output, a child of another and a
lookup in a third; each use opens its **own reader** over the file (the file is read once per use, not shared).
Aliases are case-insensitive.

## How a file is read

* **Streaming.** Rows are read forward-only with a 1 MB buffer; the parser holds one record at a time, so file
  size is not a memory limit by itself (what *indexes* a source in memory is described in
  [RunOptions.md](RunOptions.md#memory-and-temp-space)).
* **Encoding.** UTF-8, with a byte-order mark detected and removed automatically (a UTF-16/UTF-32 BOM is also
  recognized). Other encodings (Latin-1, ...) are not supported; convert the file first.
* **Header.** The first record is the header. Column names are matched **ignoring case**. If a name appears twice
  in the header, the **last** column with that name wins. Names are not trimmed.
* **Records.** Line breaks `\n`, `\r\n` and `\r` all end a record. **Blank lines are ignored.** A final record
  without a terminating newline is read. A file with no rows, or only a header, produces zero records (no output
  file is created for that output).
* **Cells.** Cell text is not trimmed. A **short row** (fewer cells than the header) is accepted: the missing
  cells read as empty. Extra cells beyond the header are ignored. Every cell is a string until a typed
  [`fields`](#fields) entry or a field `type` converts it.
* **Empty cell.** A cell with no characters is *null* when read as a field value; it is "" when read raw (for
  joins, filters, `distinctBy`, and so on). A cell with only spaces is neither: it stays as the spaces.
* **Quoting.** RFC 4180 style. A quoted cell may contain the delimiter, line breaks and the quote character
  (written doubled, `""`). **Caution:** a quote character that appears *anywhere* in a field, not only at its start
  (for example the inch mark in `12" pipe`), switches the parser into quoted mode and swallows everything up to
  the next quote, usually the rest of the file. Either quote such cells properly (`"12"" pipe"`) or pick a
  different [`quote`](#quote) character that never occurs in the data.
* **No globbing.** `file` names exactly one file. Wildcards (`orders_*.csv`) and folders are not expanded, and one
  alias cannot span several files. To read many files, concatenate them first (they must share a header), or
  declare one source per file (and one output per source).

---

### file

The path of the source file.

| | |
| --- | --- |
| **Type** | string |
| **Required** | Yes |
| **Default** | none |

#### Remarks

* A relative path is resolved against the **folder of the `.etlconfig` file**, not the working directory.
  Absolute paths are allowed. A leading `~` (or `~/`) expands to the user's home folder.
* The file must exist when the output that uses it is compiled; otherwise the run stops with
  `[FAIL] FileNotFoundException: source file not found: <resolved path>`.
* If the name ends in `.gz` and/or `.pgp` (case-insensitive) the file is decompressed/decrypted first; see
  [Encrypted and compressed sources](#encrypted-and-compressed-sources).
* A source that is declared but never referenced is never opened, so a missing file there is not an error.

#### Example

```jsonc
"orders": { "file": "../data/orders.csv" }
```

#### See also

[Load.md#folder](Load.md#folder) (the same path rules apply to `load.folder`)

---

### format

The file format.

| | |
| --- | --- |
| **Type** | string |
| **Required** | No |
| **Default** | `"delimited"` |
| **Allowed values** | `"delimited"` (case-insensitive). `"json"` and `"xml"` are reserved for future use and are rejected. |

#### Remarks

Any other value stops the run with `source format '<value>' is not supported yet (only 'delimited').` The
engine is built around an `ISourceReader` abstraction so other formats can be added; today only the delimited
reader exists.

---

### delimiter

The character that separates cells.

| | |
| --- | --- |
| **Type** | string |
| **Required** | No |
| **Default** | `","` |

| Value | Delimiter |
| --- | --- |
| `","`, `"csv"`, `"comma"`, or omitted/empty | comma |
| `"\t"` (a real tab character), `"tsv"`, `"tab"` | tab |
| `"\|"`, `"pipe"` | pipe `\|` |
| `";"`, `"semicolon"` | semicolon |
| Any other **single character** (for example `"~"`) | that character |

#### Remarks

* Shorthand names are case-insensitive (`"TSV"` works).
* A value that is not one of the above and is longer than one character fails with `unrecognized delimiter
  '<value>'.` Multi-character delimiters are not supported.
* The delimiter and the [`quote`](#quote) character must differ.

#### Example

```jsonc
"events": { "file": "events.psv", "delimiter": "pipe" }
```

See [samples/12-delimited-sources](samples/12-delimited-sources/delimited.etlconfig) (pipe, tab, semicolon and
`~`).

---

### quote

The text qualifier that wraps cells containing the delimiter, a line break or the quote character itself.

| | |
| --- | --- |
| **Type** | string (only the **first character** is used) |
| **Required** | No |
| **Default** | `"\""` (double quote) |

#### Remarks

* Inside a quoted cell, a doubled quote character is one literal quote character.
* Only the first character of the string counts; an empty string falls back to the double quote.
* There is no "no quoting" mode and no backslash escaping.
* See the stray-quote caution under [How a file is read](#how-a-file-is-read).

#### Example

```jsonc
"products": { "file": "products.txt", "delimiter": "~", "quote": "'" }
```

`'Won''t leak'` reads as `Won't leak`.

---

### hasHeader

Whether the first row is a header.

| | |
| --- | --- |
| **Type** | bool |
| **Required** | No |
| **Default** | `true` |

#### Remarks

**Currently not honored.** The property is accepted by the config loader, but the reader always treats the first
row as the header (verified: with `"hasHeader": false` the first data row is still consumed as column names).
Files must have a header row, and column names are how fields, joins and filters refer to data. For a file
without a header, add one before reading it (for example with a one-line `sed`/script).

---

### fields

Optional **input typing**: declares how selected columns are parsed. Columns not listed stay strings.

```jsonc
"fields": {
  "customer_id":    { "type": "integer" },
  "signup_ts":      { "type": "datetime", "format": "yyyy-MM-dd HH:mm:ss", "culture": "en-US" },
  "lifetime_spend": { "type": "decimal" }
}
```

| Property (per column) | Type | Default | Meaning |
| --- | --- | --- | --- |
| `type` | `string` \| `integer` \| `decimal` \| `double` \| `bool` \| `date` \| `dateTime` \| `object` \| `array` | `string` | How the cell is parsed when a field reads this column |
| `format` | string | none | `date`/`dateTime` only: exact parse format (.NET custom format such as `yyyy-MM-dd`); ISO-8601 style parsing is used when omitted |
| `culture` | string | invariant | Culture name (`"en-US"`, `"de-DE"`) used for parsing numbers and dates |

#### Remarks

* The key is the column's header name (case-insensitive). A key that matches no header is silently ignored.
* Typing happens **when a field reads the column**, and a failure then follows the usual
  [error policy](Errors.md), not a source error. An unparseable cell in a typed column that no field reads
  never causes a problem.
* An empty cell is always *null*, whatever the type.
* Typed values feed **pipelines** (`from` starts the pipeline with the typed value, and `{"field": "col"}`
  operands read typed values). **Joins, filters, `distinctBy`, `groupBy`, lookups and `mergeBy` always compare
  the raw text**, so typing a join column as `integer` does not make `007` equal `7`.
* Input fields are *typed, never transformed*. All transformation lives on output fields.
* A `type` of `object` or `array` has no useful meaning on an input column (the value stays a string).
* The parse rules of every type (rounding, culture, accepted booleans, the month-day-only date rejection) are in
  [Transforms/ValueTypes.md](Transforms/ValueTypes.md).

Declaring input types is optional: a field can also convert on its own with its output `type` or a `toInteger`
style step. The advantage of typing the *source* is that every field reading the column sees the same
parse settings (format, culture) without repeating them.

#### Example

[samples/01-basic-object/basic.etlconfig](samples/01-basic-object/basic.etlconfig) types four columns and then
maps them to `integer`, `date`, `datetime` and `decimal` output fields.

---

### decrypt

PGP settings for a source whose file name ends in `.pgp`.

| Property | Type | Required | Meaning |
| --- | --- | --- | --- |
| `pgpKeyFile` | string | No | Path to a PGP **private key** file to import into the local gpg keyring before decrypting. Relative to the config file's folder; `~` allowed. |
| `pgpPassphrase` | string | No | Passphrase of the private key. Used only when decrypting. |

Both properties are optional, and `decrypt` as a whole is only consulted for `.pgp` files. See the next section.

---

## Encrypted and compressed sources

If the configured `file` ends in `.gz`, `.pgp` or `.gz.pgp` (case-insensitive), the engine transparently turns
it into a plain CSV before reading:

| Name ends in | Steps |
| --- | --- |
| `.csv.gz.pgp` | decrypt with `gpg`, then gunzip |
| `.csv.pgp` | decrypt with `gpg` |
| `.csv.gz` | gunzip (no gpg needed) |
| anything else | read as is |

Only the trailing suffixes are examined; the file *contents* are not sniffed. The plain file keeps the name minus
those suffixes (`foo.csv.gz.pgp` becomes `foo.csv`).

### The decrypted-file cache

The plain result is written **next to the original**, in a sibling folder named `<file name>_decrypted`:

```
data/orders.csv.gz.pgp
data/orders.csv.gz.pgp_decrypted/orders.csv      <- what the engine actually reads
```

* The folder is **durable on purpose** (not a temp file) so you can inspect the plain data afterwards. It is
  never deleted by the engine. **It holds the decrypted data in the clear:** delete it when you are done and keep
  it out of version control.
* A source is unpacked **once**: the plain file is reused as long as it exists and is not older than the source
  file (by modification time), and an in-process cache skips even that check, because a source is opened many
  times in one run (once per output, relation and lookup).
* Replacing the encrypted file with a newer one makes the next run unpack it again.

### Decrypting: gpg and keys

* Decryption runs the system `gpg` binary in batch mode (`gpg --batch --yes --decrypt <file>`). GnuPG must be
  installed and on `PATH`; otherwise the run fails with `could not run 'gpg' for '<file>': ... Is GnuPG installed
  and on PATH?`
* **Normal case:** the private key is already in your gpg keyring (and your gpg-agent can unlock it). Then
  `decrypt` is not needed.
* **Importing the key:** `decrypt.pgpKeyFile` is imported with `gpg --batch --yes --import <file>` the first time
  an encrypted source is opened in the process. Importing a key you already have is harmless, so the setting may
  stay in the config permanently. A missing key file fails with `pgpKeyFile not found: <path>`; a failed import
  with `gpg failed to import key '<name>' (exit N): <gpg's message>`.
* **Passphrase:** `decrypt.pgpPassphrase` is sent to gpg on **standard input** (`--pinentry-mode loopback
  --passphrase-fd 0`), never on the command line and never logged. It is, however, stored in plain text in the
  config file: do not commit a config containing a real passphrase. Prefer a key that is already unlocked by your
  gpg-agent, or keep such configs outside the repository.
* A decryption failure (wrong key, wrong passphrase, corrupt file) stops the run:
  `gpg failed to decrypt '<file>' (exit N): <gpg's message>`.

### Trying it yourself

The repository does not ship an encrypted sample (that would mean committing a private key). To reproduce the
behavior with a throwaway key:

```sh
# make a throwaway key with an encryption subkey, and an encrypted + gzipped copy of a CSV
export GNUPGHOME=$(mktemp -d)        # keep it short: gpg's agent socket path has a length limit
gpg --batch --pinentry-mode loopback --passphrase 'sample-passphrase' \
    --quick-generate-key "Acme Sample <sample@example.com>" rsa2048 default never
gpg --batch --pinentry-mode loopback --passphrase 'sample-passphrase' \
    --quick-add-key "$(gpg --list-keys --with-colons sample@example.com | awk -F: '/^fpr/{print $10; exit}')" \
    rsa2048 encr never
gpg --batch --pinentry-mode loopback --passphrase 'sample-passphrase' \
    --armor --export-secret-keys sample@example.com > sample-private.asc
gzip -nc ../data/orders.csv > orders.csv.gz
gpg --batch --yes --trust-model always -r sample@example.com --encrypt -o orders.csv.gz.pgp orders.csv.gz
# then, in a NEW empty GNUPGHOME (so the key must be imported), run a config like:
```

```jsonc
"orders": {
  "file": "orders.csv.gz.pgp",
  "decrypt": { "pgpKeyFile": "sample-private.asc", "pgpPassphrase": "sample-passphrase" }
}
```

This was verified: the engine imported the key, decrypted, gunzipped, created
`orders.csv.gz.pgp_decrypted/orders.csv` and produced the same records as the plain `orders.csv`. Without the
`decrypt` block and without the key in the keyring the run fails with
`gpg failed to decrypt 'orders.csv.gz.pgp' (exit 2): ... No secret key`.

The gzip-only path needs no gpg and is demonstrated by
[samples/12-delimited-sources](samples/12-delimited-sources/delimited.etlconfig) (`products.csv.gz`). After a
run, that sample folder contains `products.csv.gz_decrypted/products.csv`; delete it if you do not want it
(it is not part of the expected output).

## Related

[Outputs.md](Outputs.md), [Relationships.md](Relationships.md#on), [Errors.md](Errors.md), [Load.md](Load.md)
