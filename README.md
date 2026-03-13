# Transaction Reconciliation

A .NET 10 console application that ingests a mocked JSON snapshot of card transactions, reconciles them into a SQLite database using Entity Framework Core, and maintains a full audit trail of all changes.

---

## Table of Contents

- [Overview](#overview)
- [Solution](#solution)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Core Features](#core-features)
- [Data Model](#data-model)
- [Architecture & Design](#architecture--design)
- [Configuration](#configuration)
- [How to Build and Run](#how-to-build-and-run)
- [Testing Approach](#testing-approach)
- [Assumptions](#assumptions)
- [Time Tracking](#time-tracking)

---

## Overview

This project implements a reliable hourly ingestion job for a retail payments platform. Each run fetches a 24-hour JSON snapshot of card transactions, upserts records by `TransactionId`, detects and records field-level changes, revokes transactions that have disappeared from the snapshot while still within the 24-hour window, and optionally finalizes records older than 24 hours.

The application is designed to run once per invocation (triggered by an external scheduler) and produces consistent, idempotent results when re-run with the same input.

---

## Solution

### Problem Summary

The exercise required building a reliable transaction ingestion job that:
- Fetches a JSON snapshot of the last 24 hours of card transactions from a mocked API
- Reconciles them into a persistent database with upsert logic
- Detects and records field-level changes on existing transactions
- Revokes transactions that were previously seen but are no longer in the snapshot
- Optionally finalizes records older than the 24-hour window
- Is fully idempotent — re-running with the same input produces no side effects

### My Approach

**Starting point — understanding the data lifecycle**

The first thing I mapped out was the full lifecycle of a transaction record:

```
Incoming feed → Normalize → Insert (new) or Compare (existing)
                                          ↓
                               Changed → Update + Audit
                               Unchanged → touch LastSeenAtUtc only
                                          ↓
                         Missing from feed + in window → Revoke
                         Older than 24h window → Finalize
```

This made it clear the reconciliation logic needed to operate in distinct phases rather than one monolithic loop, which shaped the structure of `ReconciliationService`.

---

**Key decisions and why**

**1. Single database transaction per run**

Wrapping the entire run in one `BeginTransactionAsync` / `CommitAsync` block ensures atomicity. If anything fails mid-run — a bad record, a network hiccup, an exception — nothing is partially committed. The next run starts from a clean state and processes the full snapshot again.

**2. Field-level change detection via `TransactionComparer`**

Rather than replacing every record on every run, I compare the incoming values against what is stored field by field. Only fields that actually changed produce an audit entry. This keeps the audit log meaningful — it tells you exactly what changed and when, rather than producing noise on every run.

The comparer is a separate static utility so it can be unit tested independently and extended without touching the service logic.

**3. Separate revocation pass**

Revocations are handled in a second pass after all upserts, not inside the upsert loop. This avoids a subtle bug: if you check for missing records while iterating inserts, newly inserted records haven't been saved yet and could be incorrectly flagged. The second pass queries the database for all in-window records and checks them against the incoming ID set.

**4. ChangeTracker for same-run finalization**

When a record with an old timestamp is inserted for the first time, it needs to be finalized in the same run. Since `SaveChanges` hasn't been called yet at that point, the record isn't in the database. I use EF Core's `ChangeTracker` to find newly added entities with old timestamps and include them in the finalization pass before the commit.

**5. `IClock` abstraction**

The system clock is injected as `IClock` rather than called via `DateTime.UtcNow` directly. In tests, a `TestClock` with a fixed timestamp is injected instead. This makes all time-dependent logic — cutoff calculation, `CreatedAtUtc`, `UpdatedAtUtc`, finalization — fully deterministic and reproducible.

**6. Card data protection**

Raw card numbers are hashed with SHA-256 immediately on ingestion via `CardDataProtector` and never written to the database. Only the hash and last 4 digits are stored. This satisfies basic PCI-DSS-style data minimization and means the database can be shared or inspected without exposing sensitive card data.

---

**What idempotency means in practice**

Re-running the same feed twice produces:
- No duplicate `Transactions` rows (upsert by `TransactionId`)
- No duplicate `TransactionAudit` entries (only written when something actually changed)
- No spurious status changes (revoked records stay revoked; finalized records are skipped entirely)

This is verified directly by `IdempotencyTests.cs`.

---

**Trade-offs and what I would do differently at scale**

| Area | Current approach | At scale |
|---|---|---|
| Feed source | Local JSON file via `MockTransactionFeedClient` | Replace with HTTP client behind `ITransactionFeedClient` — no other code changes needed |
| Database | SQLite file | Swap connection string to PostgreSQL or SQL Server — EF Core migrations handle the rest |
| Scheduling | Single-run console app | Wrap in an Azure Function timer trigger or a Hangfire job |
| Audit storage | Same SQLite DB | Move audit table to append-only cold storage (e.g. Azure Table Storage) to avoid bloat |
| Card hashing | SHA-256 | Replace with HMAC-SHA256 with a secret key stored in Key Vault for stronger protection |

---

## Tech Stack

| Technology | Version | Purpose |
|---|---|---|
| .NET | 10 | Runtime and SDK |
| C# | 13 | Language |
| Entity Framework Core | 10.0.4 | ORM and code-first migrations |
| SQLite | via EF Core | Embedded database |
| xUnit | 2.x | Unit and integration tests |
| Microsoft.Extensions.Hosting | 10.0.4 | Dependency injection and configuration |

---

## Project Structure

```
transactionreconsilation/
├── src/
│   └── TransactionReconciliation.Console/
│       ├── Configuration/
│       │   ├── FeedOptions.cs              # JSON feed path config
│       │   └── ProcessingOptions.cs        # LookbackHours, EnableFinalization
│       ├── Data/
│       │   ├── AppDbContext.cs             # EF Core DbContext
│       │   ├── DbInitializer.cs            # Applies migrations on startup
│       │   └── Mocks/
│       │       ├── mock-transactions.json  # Run 1 sample data
│       │       └── mock-transactions-run2.json  # Run 2 sample data
│       ├── Domain/
│       │   ├── Entities/
│       │   │   ├── TransactionRecord.cs    # Main transaction entity
│       │   │   └── TransactionAudit.cs     # Audit log entity
│       │   ├── Enums/
│       │   │   ├── TransactionStatus.cs    # Active, Revoked, Finalized
│       │   │   └── AuditChangeType.cs      # Insert, Update, Revoked, Finalized
│       │   └── Models/
│       │       ├── IncomingTransactionDto.cs   # Incoming JSON shape
│       │       ├── NormalizedTransaction.cs    # Internal normalized form
│       │       └── FieldChange.cs              # Field-level diff result
│       ├── Migrations/                     # EF Core code-first migrations
│       ├── Services/
│       │   ├── Interfaces/
│       │   │   ├── IReconciliationService.cs
│       │   │   ├── ITransactionFeedClient.cs
│       │   │   ├── ICardDataProtector.cs
│       │   │   └── IClock.cs
│       │   ├── ReconciliationService.cs    # Core reconciliation logic
│       │   ├── MockTransactionFeedClient.cs # Reads JSON mock feed
│       │   ├── CardDataProtector.cs        # Hashes card numbers
│       │   └── SystemClock.cs              # Production clock wrapper
│       ├── Utilities/
│       │   └── TransactionComparer.cs      # Field-by-field diff
│       ├── Program.cs                      # Host setup and DI wiring
│       └── appsettings.json
└── tests/
    └── TransactionReconciliation.Tests/
        ├── TestHelpers/
        │   ├── SqliteTestDbHelper.cs       # In-memory SQLite setup
        │   └── TestClock.cs                # Fixed-time clock for tests
        ├── ReconciliationServiceTests.cs   # Insert and update tests
        ├── RevocationTests.cs              # Revocation tests
        └── IdempotencyTests.cs             # Idempotency tests
```

---

## Core Features

- **Upsert by `TransactionId`** — new records are inserted; existing records are compared field by field
- **Field-level change detection** — only meaningful changes trigger an update and audit entry
- **Full audit trail** — every insert, update, revocation, and finalization is recorded in `TransactionAudits`
- **Revocation** — transactions present in a previous run but absent from the current snapshot (while still within the 24-hour window) are marked as `Revoked`
- **Finalization** — transactions older than the lookback window are marked as `Finalized` and will not change on subsequent runs
- **Idempotency** — re-running with the same input produces no duplicate records or spurious audit entries
- **Single DB transaction** — the entire reconciliation run is wrapped in one database transaction for atomicity
- **Card data protection** — raw card numbers are never persisted; only a SHA-256 hash and the last 4 digits are stored

---

## Data Model

### `Transactions` table

| Column | Type | Notes |
|---|---|---|
| `TransactionId` | TEXT (PK) | Stable unique identifier |
| `CardHash` | TEXT | SHA-256 hash of the card number |
| `CardLast4` | TEXT | Last 4 digits of the card number |
| `LocationCode` | TEXT | Point-of-sale location |
| `ProductName` | TEXT | Product purchased |
| `Amount` | DECIMAL(18,2) | Transaction amount |
| `TransactionTimeUtc` | DATETIME | When the transaction occurred |
| `Status` | INTEGER | 1=Active, 2=Revoked, 3=Finalized |
| `CreatedAtUtc` | DATETIME | When first inserted |
| `UpdatedAtUtc` | DATETIME | Last modified time |
| `LastSeenAtUtc` | DATETIME | Last time seen in a feed snapshot |
| `RevokedAtUtc` | DATETIME? | When revoked (nullable) |
| `FinalizedAtUtc` | DATETIME? | When finalized (nullable) |

### `TransactionAudits` table

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER (PK) | Auto-increment |
| `TransactionId` | TEXT | References the transaction |
| `ChangeType` | INTEGER | 1=Insert, 2=Update, 3=Revoked, 4=Finalized |
| `FieldName` | TEXT? | Which field changed (for updates) |
| `OldValue` | TEXT? | Previous value |
| `NewValue` | TEXT? | New value |
| `RunId` | TEXT | Unique ID for the reconciliation run |
| `ChangedAtUtc` | DATETIME | When the change was recorded |

---

## Architecture & Design

The application follows a clean layered structure with dependency injection throughout.

### Reconciliation Pipeline

Each run executes the following steps inside a single database transaction:

1. **Load** — fetch the JSON snapshot via `ITransactionFeedClient`
2. **Normalize** — parse and deduplicate incoming records, hash card numbers
3. **Upsert** — for each incoming transaction:
   - If new → insert with `Status = Active` and write an `Insert` audit entry
   - If existing and finalized → skip entirely
   - If existing with changes → apply updates and write `Update` audit entries per changed field
   - If existing with no changes → update `LastSeenAtUtc` only
4. **Revoke** — find in-window records absent from the snapshot and mark as `Revoked`
5. **Finalize** — if enabled, mark records older than the lookback window as `Finalized`
6. **Commit** — save all changes in one atomic database transaction

### Key Design Decisions

**`IClock` abstraction** — the system clock is injected rather than called directly. This makes time-dependent logic fully testable with a fixed `TestClock` in tests.

**`ITransactionFeedClient` interface** — the feed source is abstracted behind an interface. The mock implementation reads from a local JSON file. A real implementation would call an HTTP API.

**`CardDataProtector`** — raw card numbers are hashed immediately on ingestion and never written to the database, satisfying PCI-DSS-style data minimization.

**`TransactionComparer`** — field comparison is isolated in a dedicated static utility, making it easy to add or remove tracked fields without touching the service logic.

**Single DB transaction per run** — all inserts, updates, revocations, and finalizations commit together or roll back together, ensuring the database is never left in a partially-processed state.

**ChangeTracker for same-run finalization** — newly inserted old records are detected via EF Core's `ChangeTracker` before `SaveChanges`, ensuring they are finalized in the same run they are first seen.

---

## Configuration

All configuration lives in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=transactions.db"
  },
  "TransactionFeed": {
    "JsonFilePath": "Data/Mocks/mock-transactions.json"
  },
  "Processing": {
    "EnableFinalization": true,
    "LookbackHours": 24
  }
}
```

| Setting | Description |
|---|---|
| `DefaultConnection` | SQLite database file path |
| `JsonFilePath` | Path to the mock JSON feed file (relative to the output directory) |
| `EnableFinalization` | Whether to finalize records older than the lookback window |
| `LookbackHours` | How many hours back to consider transactions "in-window" |

---

## How to Build and Run

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

---

### Step 1 — Clone the repository

```powershell
git clone https://github.com/shiva9596/transaction-reconciliation.git
cd transaction-reconciliation
```

---

### Step 2 — Restore dependencies

```powershell
dotnet restore
```

---

### Step 3 — Build the solution

```powershell
dotnet build
```

Expected output:
```
Build succeeded.
```

---

### Step 4 — Run the application (Run 1)

Before the first run, delete any existing database file to start clean:

```powershell
Remove-Item .\transactions.db -Force -ErrorAction SilentlyContinue
```

> **Why?** The app uses a SQLite file called `transactions.db` in the project root. If a stale database exists from a previous run, it will affect the results. Deleting it ensures a clean state before each demo.

Then run the app:

```powershell
dotnet run --project .\src\TransactionReconciliation.Console\TransactionReconciliation.Console.csproj
```

Expected output at the end of the log:
```
Completed reconciliation run. Inserted: 4, Updated: 0, Revoked: 0, Finalized: 1
```

This means:
- TXN-1001, TXN-1002, TXN-1003 were inserted as new Active transactions
- TXN-OLD-001 was inserted and immediately finalized (its timestamp is older than 24 hours)

---

### Step 5 — Run the tests

```powershell
dotnet test
```

Expected output:
```
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4
```

> Tests use an in-memory SQLite database and do **not** affect or require the `transactions.db` file.

---

### Step 6 — Run 2 (optional demo of update and revocation)

Switch the feed file in `appsettings.json`:

```json
"JsonFilePath": "Data/Mocks/mock-transactions-run2.json"
```

**Do not delete the database** — Run 2 depends on the records inserted in Run 1.

```powershell
dotnet run --project .\src\TransactionReconciliation.Console\TransactionReconciliation.Console.csproj
```

Expected output:
```
Completed reconciliation run. Inserted: 0, Updated: 1, Revoked: 1, Finalized: 0
```

---

### Step 7 — Run 3 (optional idempotency check)

Re-run with the same feed as Run 2, without resetting the database:

```powershell
dotnet run --project .\src\TransactionReconciliation.Console\TransactionReconciliation.Console.csproj
```

Expected output:
```
Completed reconciliation run. Inserted: 0, Updated: 0, Revoked: 0, Finalized: 0
```

This confirms idempotent behaviour — re-running with unchanged input produces no side effects.



---

## Testing Approach

Tests are written with xUnit and use an in-memory SQLite database to exercise real EF Core behavior without a file on disk.

| Test file | What it covers |
|---|---|
| `ReconciliationServiceTests.cs` | Inserting a new transaction; detecting and recording field-level updates |
| `RevocationTests.cs` | Marking in-window transactions as revoked when absent from the snapshot |
| `IdempotencyTests.cs` | Ensuring re-runs with unchanged input produce no duplicate records or audit entries |

A `TestClock` with a fixed timestamp is injected in all tests to make time-based logic (cutoff calculation, finalization) fully deterministic.

---

## Assumptions

- `TransactionId` is treated as a `string` rather than `int`. The exercise specification shows `int` in the schema but the sample JSON uses string identifiers like `T-1001`. String was chosen for flexibility and to match real-world payment identifiers.
- The JSON feed represents the authoritative current snapshot for the last 24 hours. Transactions absent from the snapshot but within the window are assumed to have been cancelled or voided.
- Input ordering is not meaningful — the feed may arrive out of order.
- Finalization is optional and controlled via `Processing:EnableFinalization` in configuration.
- Raw card numbers are never persisted. Only a SHA-256 hash and the last 4 digits are stored.
- Duplicate `TransactionId` values within the same snapshot feed are deduplicated during normalization, keeping the first occurrence.
- The application is designed to be triggered by an external scheduler (e.g. a cron job or Azure Function timer). No internal scheduling is implemented.

---
