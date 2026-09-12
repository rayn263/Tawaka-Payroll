# Architecture Decision Record

Append-only. Each decision keeps its number forever. Superseding a decision means adding a new
entry that references the old one, never editing or deleting history.

Status values: `Proposed` · `Accepted` · `Superseded by ADR-nnn` · `Rejected`

---

### ADR-001 — Windows desktop application on .NET 8 with a Blazor Hybrid UI
**Status:** Proposed
**Context:** A Zimbabwean SME needs payroll that works without reliable internet, looks like
commercial accounting software, and may later move to a server or the cloud.
**Decision:** WPF shell (`net8.0-windows`) hosting Blazor components via WebView2. Domain and
engine are plain .NET and UI-agnostic.
**Consequences:** Modern styling without fighting XAML; UI portable to ASP.NET Core later; adds a
WebView2 runtime dependency, which is present on current Windows and bundled by the installer.

### ADR-002 — Money is a value object; no bare decimals, no floats
**Status:** Proposed
**Decision:** `Money { decimal Amount; CurrencyCode Currency; }`. Cross-currency arithmetic throws.
Persisted as a scaled integer on SQLite, `DECIMAL(18,4)` elsewhere.
**Consequences:** Whole classes of currency-mixing bugs become compile-time or runtime failures
rather than wrong payslips. Slightly more ceremony in code.

### ADR-003 — Every statutory value is a dated, versioned rule row
**Status:** Proposed
**Decision:** No statutory constant in code. Rules carry `EffectiveFrom`/`EffectiveTo`, currency,
status and source reference. A missing rule is a blocking error, never a default.
**Consequences:** Legislative change is configuration, not a release. Historical payroll stays
correct. Requires disciplined seeding and a verification workflow.

### ADR-004 — Payroll runs snapshot their rules and exchange rates
**Status:** Proposed
**Decision:** On calculation, the resolved rule set and rates are persisted with the run by FK and
as immutable JSON.
**Consequences:** Reproducible history; later rule edits cannot rewrite past payroll. Costs some
storage per run — a price worth paying.

### ADR-005 — The calculation engine is pure and has no database access
**Status:** Proposed
**Decision:** Engine input is an immutable snapshot; output is a result plus a full trace. No I/O,
no clock, no current user.
**Consequences:** Exhaustively testable, deterministic and auditable; the single most important
decision for correctness.

### ADR-006 — Audit and period locking are enforced by EF Core interceptors
**Status:** Proposed
**Decision:** A `SaveChanges` interceptor writes audit rows and refuses writes to locked periods.
**Consequences:** Cannot be bypassed by a screen or a future import that forgets to check.

### ADR-007 — Statutory payment status is a four-state machine backed by payment records
**Status:** Proposed
**Decision:** CALCULATED, DEDUCTED, APPROVED, PAID are independent, separately permissioned flags.
`IsPaid` can only be set by the existence of a `StatutoryPayments` row with date, method and
reference. Partial payments supported.
**Consequences:** The system structurally cannot misrepresent compliance. This is a deliberate
constraint on the software, not merely a UI convention.

### ADR-008 — Multi-currency tax methodology is a configurable, advisor-signed strategy
**Status:** Proposed
**Context:** Secondary sources contradict each other on whether USD and ZiG earnings are aggregated
and converted or taxed separately (see `COMPLIANCE_ZIMBABWE.md` §3).
**Decision:** Model it as a versioned `CurrencyTaxStrategies` rule recorded on every run, seeded to
the more conservative aggregate method and flagged `Unverified` until an advisor signs off.
**Consequences:** No methodology is invented. If the interpretation changes, affected runs are
identifiable and can be recalculated deliberately.

### ADR-009 — QuestPDF licensing must be confirmed
**Status:** Proposed
**Decision:** Payslips render through the Blazor template to PDF via WebView2 (no third-party
licence). QuestPDF is used only for fixed-layout statutory forms; confirm Community licence
eligibility or budget for a commercial licence before it enters the build.

### ADR-010 — Multi-company structure from day one, single-company UI until Phase 3
**Status:** Proposed
**Decision:** Every scoped table carries `CompanyId` now; the interface exposes one company until
Phase 3.
**Consequences:** Avoids a painful migration later at almost no present cost.

### ADR-011 — Names are frozen once established
**Status:** Proposed
**Decision:** Table, column, entity, enum and public engine type names change only via a new ADR.
Additive change is always preferred to renaming.
**Consequences:** Two AI assistants and a human can work on this codebase without silently
breaking each other.
