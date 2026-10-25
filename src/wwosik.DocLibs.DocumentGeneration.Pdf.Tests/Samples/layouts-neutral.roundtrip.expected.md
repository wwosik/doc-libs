## Page 1

Shipping schedules, rebuilt: documents instead of tables

## Page 2

### Why rebuild the planner?

### Configuration is too complicated

### 26 tables

for 12 kinds of rule; the formula hides in the structure.

### Large schedules are too slow

### 3.2 GB

of memory per schedule at 5,000 stops, mostly object overhead.

Fixed along the way

- Locking – the join counts again without a lock
- Duplicates – the export guard filters out every row
- Provenance – results do not record their inputs

### Our data are documents

Once created, the data hardly change: they are read, extended and recalculated. What Today Harbour

Booking Booking tables, row by row One document per revision Calculation Result tables One document per version, write-once Rules 26 tables One document with formulas Note

To be fair: bookings are still edited after intake – hence revisions rather than strict immutability.

### What it brings

- Really large schedules – documents are written in pages and streamed
- More flexibility for less effort – new pricing logic is a formula, not a table and a migration
- Reproducible results – every calculation records its inputs
- Simpler infrastructure – storage only, no database, no message broker

### Storage without a database

Blob storage is the only external dependency.

### Documents in blob storage

- write-once results, rule versions
- revisions bookings, reference data
- latest-wins only the rule header

09.03.2026 2 / 3

## Page 3

### Tables for status and lists

Bookings Schedules WorkQueue RuleVersions Hosts Audit

### Every result has a manifest

Booking revision, rule version, parameter revision, and a revision per reference source.

### Status model

Status Moved on by

Created System

WaitingForApproval Human

Calculated System

Failed Human

Rows in the highlight colour need a human.

### Processing without a broker

A broker guarantees delivery at least once – not processing at most once.

The race in the old system happened with a broker.

- The queue is a table – claimed by compare-and-swap, with leases
- A broker means twice the machinery – we need compare-and-swap anyway

### Status and next steps

Part Status Host, storage, identifiers Done

Intake and validation Done

Work queue and detail pages In review Engine and results Next

Important

Then a shadow run: Harbour calculates the same bookings, and the results are compared.

### Two views

Before: every change waited for the weekly release.

After: changes ship the same day, behind a switch, and the old path stays until the numbers agree.

09.03.2026 3 / 3
