# Input CSV formats (products.csv, identified.csv, anonymous.csv)

This is the canonical description of the three CSV files read by the commands
[Known (identified) purchases](Readme.md), [Anonymous purchases](../PurchaseAnonymous/Readme.md) and
[Generate Email Conversion data](../GenerateEmailConversions/Readme.md). The parsing is implemented in
`bleak.Data.InfiniteDataLoader/CsvLoader.cs` (`ReadProducts`, `ReadIdentified`, `ReadPeople`). The identified.csv layout changed (four columns now); see [identified.csv](#identifiedcsv).

Runnable examples: [samples/inputs/](samples/inputs/).

## Where the files live

All three files are read from the **LocalData** folder, resolved by `DataPaths.Root()`:

1. `<four directories above the app's bin folder>/LocalData` if that directory exists (for a normal build,
   `DataMonger/bin/Debug/net10.0` -> the repository root, so `<repo>/LocalData`);
2. otherwise `LocalData` under the current working directory.

| File | Read by | When |
|---|---|---|
| `products.csv` | `Program.cs` at **startup** | Always, before the menu is shown, for every command. A missing or malformed file aborts the app with an unhandled exception even if you just choose `q`. |
| `identified.csv` | Known (identified) purchases, Generate Email Conversion data | When the command runs. |
| `anonymous.csv` | Anonymous purchases | When the command runs. |

## Parsing rules common to all three files

| Rule | Behavior |
|---|---|
| Header | There is no header detection: the **first line is always discarded**, whatever it contains. Column names are never read; columns are **positional**. A file with no header would silently lose its first record. |
| Delimiter | A bare comma. There is **no quoting or escaping** (no RFC 4180 support): a field containing a comma is split in two. The one exception is the last `products.csv` column (see below). |
| Encoding | .NET `StreamReader` default: UTF-8 (BOM detected). Line endings `\n` or `\r\n` both work. |
| Whitespace | Values are not trimmed. |
| Blank lines | Blank or whitespace-only lines anywhere after the header are **skipped**. |
| Short rows | A row with fewer columns than required throws `InvalidDataException` with the message `<file>, line <n>: expected at least <k> comma-separated columns but found <m>.` (`n` is the 1-based physical line, counting the header and blank lines). The exception is unhandled and ends the command/app. |
| Extra columns | Ignored for `identified.csv` and `anonymous.csv`. |
| Numeric parsing | Invariant culture (`.` decimal separator), `products.csv` only. A bad price or quantity throws `InvalidDataException` naming the file, line and column. |
| Duplicates / validation | None. Duplicate rows produce duplicate requests. |
| Size | `products.csv` is read fully into memory at startup; the people files are read fully into a list when the command starts. |

## products.csv

Nine positional columns. Because the line is split into at most nine parts, the **last column (`Url`) may itself contain
commas**; no other column may.

| # | Column (suggested header) | Type | Used as |
|---|---|---|---|
| 1 | `sku` | string | `items[].id` (purchase commands) / `sku` (generated events) |
| 2 | `resource_type` | string | `items[].resourceType` (purchase commands only; unused by the generator) |
| 3 | `price` | decimal | `items[].price` / `unit_price` in generated events |
| 4 | `quantity` | integer | `items[].quantity` (taken from the file, **not** randomized) |
| 5 | `product_name` | string | `items[].productname` |
| 6 | `category` | string | `items[].category` |
| 7 | `subcategory` | string | `items[].subcategory`; for Generate Email Conversion data, `Kids` or `Baby` (case-insensitive) on a campaign's featured product makes its brand `Acme Kids` |
| 8 | `size` | string | `items[].size` |
| 9 | `url` | string | `items[].url`; the click event's target link |

```csv
sku,resource_type,price,quantity,product_name,category,subcategory,size,url
P-2001,product,59.95,1,Acme Trail Jacket,Outerwear,Mens,M,https://shop.example.com/p/2001
```

A header-only products file is accepted by the loader. Generate Email Conversion data then fails with
`[FAIL] products.csv is empty`, while the purchase commands send an empty `items` array (they pick `Take(n)` from an
empty list; not run, derived from the code).

## identified.csv

Four positional columns, read by `CsvLoader.ReadIdentified` into `IdentifiedPerson(FirstName, LastName, EmailAddress, UserId)`.

| # | Column (suggested header) | Type | Used as |
|---|---|---|---|
| 1 | `first_name` | string | first name (identify call; generator row) |
| 2 | `last_name` | string | last name |
| 3 | `email` | string | email address: client identifier and email contact in the identify call, `recipient.email` / `recipient_email` in the generated events |
| 4 | `user_id` | string | the customer's id: the key linking the identify call and the purchase (Known purchases) and `user_id` in the generated events |

```csv
first_name,last_name,email,user_id
Ada,Lovelace,ada.lovelace@example.com,1001
```

Extra columns are ignored. **Layout change:** earlier versions had eight columns: the same first three,
then an external id, the user id, and three per-customer program columns. The external id and the program columns were
removed and the user id became column 4 (`user_id`). An old eight-column file is not
rejected, but column 4 (the old external id) would now be read as the user id, so migrate it: keep columns 1 to 3 and
replace column 4 with the old `uid` value, dropping the rest.

## anonymous.csv

Three positional columns, read by `CsvLoader.ReadPeople`.

| # | Column (suggested header) | Type | Used as |
|---|---|---|---|
| 1 | `first_name` | string | read but **not sent** by Anonymous purchases |
| 2 | `last_name` | string | read but **not sent** |
| 3 | `email` | string | the only identifier sent (`unique_client_id`) |

```csv
first_name,last_name,email
Charles,Babbage,charles.babbage@example.com
```

## See also

[Known (identified) purchases](Readme.md) | [Anonymous purchases](../PurchaseAnonymous/Readme.md) |
[Generate Email Conversion data](../GenerateEmailConversions/Readme.md)
