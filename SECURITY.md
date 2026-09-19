# Tawaka Payroll — security

What is protected, how, and what is deliberately not attempted.

---

## 1. The model in one paragraph

Tawaka Payroll is a single-user desktop application holding one company's payroll in a SQLite
database inside the signed-in Windows user's profile. It does not listen on a network, has no
server component and no remote access. Its security model is therefore about **who did what**
inside the application, and about making the record of that impossible to alter from within it —
not about network attackers.

**Anyone with the Windows account, or with the database file, has the payroll data.** Protecting
the machine and the backups is the operator's job: full-disk encryption, a Windows password, and
backups kept somewhere as protected as the machine.

---

## 2. Authentication

| | |
|---|---|
| Password storage | PBKDF2-HMAC-SHA256, **210,000 iterations**, 128-bit per-user salt, constant-time comparison |
| Plaintext | Never stored, never logged, never in the audit trail |
| First administrator | Password **generated**, shown once, never defaulted. There is no default credential anywhere in the codebase |
| Forced change | Every account — the first administrator and every account created since — must change its password at first sign-in and cannot get past that screen |
| Lockout | Five failed attempts locks the account for fifteen minutes; an administrator can clear it |
| Enumeration | An unknown username and a wrong password give the same message. A failed attempt never reveals whether the account exists |
| Every attempt | Recorded, successful or not, with the machine name |
| Sign-out | Recorded |
| Inactive accounts | Cannot sign in |
| Rehashing | A hash below the current iteration count is upgraded on the next successful sign-in |

Nothing in the application is reachable before signing in: the root component renders the sign-in
screen and nothing else, so no payroll action can be attributed to an anonymous user.

---

## 3. Authorisation

Thirty-nine permissions in eight categories, held by roles, held by users. Every service checks the
permission before doing the work, not after. Four roles ship: Administrator, Payroll Officer,
Manager, Viewer.

### Segregation of duties

Certain permissions are declared as conflicting, and the conflict is enforced rather than
documented:

| Cannot be held together | Because |
|---|---|
| `Payroll.Calculate` / `Payroll.Approve` | One person cannot prepare and accept the same payroll |
| `Time.Edit` / `Time.Approve` | One person cannot capture and approve the same timesheet |
| `Leave.Edit` / `Leave.Approve` | One person cannot book and approve their own leave |
| `Loans.Edit` / `Loans.Approve` | One person cannot raise and approve a loan |

Enforced in two places: a role cannot be saved holding a conflicting pair, and a **user cannot be
given two roles that between them** hold one. Payroll approval additionally refuses the specific
person who calculated that run, by identity — so holding the permission is not enough.

### Administration

An administrator can create a user, set and change their roles, reset a password, disable and
re-enable an account, clear a lockout, and read the audit trail. There is no superuser bypass: an
administrator is subject to the same segregation rules, cannot approve a payroll they calculated,
cannot disable their own account, and cannot remove the last account able to manage users.

A user is never deleted — their name is on the payrolls they calculated and the approvals they gave.

---

## 4. Integrity

| | |
|---|---|
| Audit trail | Append-only, one row per changed field, with actor, time, machine, old and new value. Nothing in the application edits or deletes an entry |
| Redaction | Password hashes and salts are excluded from the audit trail |
| Locking | A locked payroll's run, results, lines, obligations, snapshot and consumed inputs are immutable **at the data layer**, so no screen, import or script inside the application can change them |
| Input snapshots | Sealed and SHA-256 hashed at the moment of calculation. A stored snapshot whose content no longer matches its hash is refused rather than recalculated |
| Backups | Verified against their manifest before a restore; a tampered or foreign folder is refused, and the displaced database is kept |
| Company scoping | Every company-owned row carries its company, and every list query filters by it |

---

## 5. What is deliberately not attempted

- **Encryption at rest.** The SQLite database is not encrypted. Use Windows full-disk encryption.
- **Network security.** There is no network surface to secure.
- **Multi-user concurrency.** One person at a time, one database.
- **Role permission editing.** The four shipped roles are what an installation has;
  `RoleService` can change a role's permissions but no screen exposes it (see `PROJECT_STATE.md`).
- **Password complexity beyond the default policy** — ten characters, upper, lower and a digit.
- **Two-factor authentication**, single sign-on, or account recovery by email.

---

## 6. Reporting a problem

This is a release candidate that has never run on Windows. Anything found during the validation in
`docs/WINDOWS_VALIDATION.md` should be reported with the screen it happened on and the contents of
`%LOCALAPPDATA%\Tawaka Payroll\logs\`.
