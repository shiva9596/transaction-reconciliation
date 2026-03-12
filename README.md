# Transaction Reconciliation

## Overview
This project is a .NET 10 console application that ingests a mocked JSON snapshot of transactions from the last 24 hours, reconciles them into a SQLite database using Entity Framework Core, records audit history, revokes missing in-window records, optionally finalizes records older than 24 hours, and supports idempotent reruns.

The solution was implemented as a maintainable reconciliation job with clear separation between configuration, persistence, domain models, services, and tests.

## Tech Stack
- .NET 10
- C#
- Entity Framework Core
- SQLite
- xUnit

## Project Structure
- `src/TransactionReconciliation.Console` - main console application
- `tests/TransactionReconciliation.Tests` - automated tests

## Core Features
- Upsert transactions by `TransactionId`
- Detect field-level changes for existing transactions
- Write audit history for inserts, updates, revocations, and finalizations
- Mark missing in-window transactions as revoked
- Optionally finalize records older than the 24-hour window
- Support idempotent reruns
- Configuration through `appsettings.json`

## Design Summary
The solution is structured as a small reconciliation pipeline.

- `TransactionRecord` stores the current reconciled state of each transaction
- `TransactionAudit` stores audit history
- `MockTransactionFeedClient` reads the mocked JSON snapshot
- `ReconciliationService` performs the main reconciliation workflow
- `CardDataProtector` derives card hash and last four digits instead of persisting raw card numbers
- `TransactionComparer` performs field-by-field comparison
- A clock abstraction was introduced to support deterministic tests for time-based logic

Each reconciliation run:
1. Loads the current 24-hour snapshot
2. Normalizes incoming transactions
3. Inserts new transactions
4. Updates existing transactions only when tracked fields changed
5. Records audit entries for meaningful changes
6. Revokes in-window transactions missing from the current snapshot
7. Optionally finalizes records older than 24 hours
8. Commits the run inside a single database transaction

## Assumptions
- `TransactionId` is treated as a string rather than int, as the sample JSON uses string identifiers like `TXN-1001`. This was a deliberate decision for flexibility.
- The JSON feed represents the authoritative current snapshot for the last 24 hours
- Input ordering is not meaningful
- Finalization is optional and controlled through configuration
- Raw card numbers are not persisted in the database model
- Duplicate `TransactionId` values in the same snapshot are deduplicated during normalization

## Database
The project uses EF Core code-first with SQLite.

### Main Tables
- `Transactions` - current state of each transaction
- `TransactionAudits` - history of changes

### Migration
An initial EF Core migration is included in the `Migrations` folder.

## Configuration
The application reads settings from `appsettings.json`.

Configured sections:
- `ConnectionStrings:DefaultConnection`
- `TransactionFeed:JsonFilePath`
- `Processing:EnableFinalization`
- `Processing:LookbackHours`

## How to Run

### Restore dependencies
```powershell
dotnet restore
```

### Build the solution
```powershell
dotnet build
```

### Run the application
```powershell
dotnet run --project .\src\TransactionReconciliation.Console\TransactionReconciliation.Console.csproj
```

### Run tests
```powershell
dotnet test
```

## Demo Scenarios

The mock feed is designed to demonstrate all reconciliation features across two runs.

**Run 1** (default `mock-transactions.json`):
- TXN-1001, TXN-1002, TXN-1003 are inserted as new Active transactions
- TXN-OLD-001 timestamp is older than 24 hours — inserted then immediately finalized

Expected output:
```
Inserted: 4, Updated: 0, Revoked: 0, Finalized: 1
```

**Run 2** — switch the feed path in `appsettings.json` to use the second mock file:
```json
"JsonFilePath": "Data/Mocks/mock-transactions-run2.json"
```
- TXN-1001 is updated (ProductName and Amount changed)
- TXN-1002 is revoked (missing from snapshot, still within 24-hour window)
- TXN-1003 is unchanged — no duplicate audit entries created (idempotent)

Expected output:
```
Inserted: 0, Updated: 1, Revoked: 1, Finalized: 0
```

## Testing Approach

The automated tests focus on the highest-risk business rules:

- Inserting a new transaction
- Updating an existing transaction when tracked fields change
- Revoking a missing in-window transaction
- Ensuring idempotent reruns do not create duplicate transactions or duplicate audit entries

Tests use an in-memory SQLite database to exercise real EF Core behavior, and a fake clock to make time-based rules deterministic.

## Time Tracking
- Estimated: X hours
- Actual: Y hours