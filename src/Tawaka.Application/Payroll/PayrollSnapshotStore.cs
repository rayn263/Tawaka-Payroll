using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Domain.Payroll;
using Tawaka.Payroll.Engine.Inputs;

namespace Tawaka.Application.Payroll;

/// <summary>
/// Stores the exact snapshot each calculation ran on, and seals it.
/// <para>
/// Milestone 3 made the snapshot immutable in memory; that is no longer sufficient. Once payroll
/// consumes approved time, leave and a loan balance, those inputs move on: recalculating September
/// in March would find a smaller loan balance and produce a different, equally defensible figure.
/// Storing the snapshot means a completed run can be reproduced against the inputs it actually had
/// (ADR-035).
/// </para>
/// </summary>
public sealed class PayrollSnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Deterministic on purpose: the hash is only meaningful if the same snapshot always
        // serialises to the same bytes.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // The rule graph has parent back-references — a tax bracket points at its table, which
        // points back at its brackets. Ignoring the cycle drops the redundant back-reference
        // rather than the data: every figure is still there, once.
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters =
        {
            new JsonStringEnumConverter(),

            // Money and its currency are value types with no settable members; without these the
            // currency comes back empty and the snapshot is worse than useless.
            new CurrencyCodeJsonConverter(),
            new MoneyJsonConverter()
        }
    };

    private readonly IPayrollDataContext _context;
    private readonly IClock _clock;

    public PayrollSnapshotStore(IPayrollDataContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public static string Serialise(PayrollInputSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, SerializerOptions);

    public static string Hash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    /// <summary>
    /// Captures a snapshot against a run employee and seals it immediately: sealing marks the
    /// moment calculation began, after which nothing may rewrite what the engine was given.
    /// </summary>
    public PayrollInputSnapshotRecord Capture(
        Guid runId, Guid runEmployeeId, PayrollInputSnapshot snapshot, string engineVersion)
    {
        var json = Serialise(snapshot);
        var now = _clock.Now;

        var record = new PayrollInputSnapshotRecord
        {
            PayrollRunId = runId,
            PayrollRunEmployeeId = runEmployeeId,
            EmployeeId = snapshot.EmployeeId,
            SnapshotJson = json,
            SnapshotHash = Hash(json),
            EngineVersion = engineVersion,
            CapturedAt = now,
            SealedAt = now
        };

        _context.PayrollInputSnapshots.Add(record);

        foreach (var input in snapshot.ApprovedInputs)
        {
            _context.PayrollRunInputSources.Add(new PayrollRunInputSource
            {
                PayrollRunId = runId,
                PayrollRunEmployeeId = runEmployeeId,
                InputType = input.InputType,
                InputId = input.InputId,
                Description = input.Description,
                ApprovedBy = input.ApprovedBy,
                ApprovedAt = input.ApprovedAt
            });
        }

        return record;
    }

    /// <summary>
    /// Reads back a stored snapshot, refusing one whose content no longer matches its hash. A
    /// mismatch means the row was altered outside the application, and silently calculating from
    /// it would produce a figure nobody could account for.
    /// </summary>
    public async Task<PayrollInputSnapshot?> ReadAsync(
        Guid runEmployeeId, CancellationToken cancellationToken = default)
    {
        var record = await _context.PayrollInputSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PayrollRunEmployeeId == runEmployeeId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        if (Hash(record.SnapshotJson) != record.SnapshotHash)
        {
            throw new InvalidOperationException(
                $"The stored payroll input snapshot for run employee {runEmployeeId} does not " +
                "match its hash. It has been altered since it was sealed and cannot be trusted.");
        }

        return Deserialise(record.SnapshotJson);
    }

    /// <summary>
    /// Deserialises a stored snapshot. Unlike <see cref="ReadAsync"/> this does not check the
    /// hash, so the caller must — a snapshot that no longer matches its hash has been altered.
    /// </summary>
    public static PayrollInputSnapshot? Deserialise(string json) =>
        JsonSerializer.Deserialize<PayrollInputSnapshot>(json, SerializerOptions);

    /// <summary>Every approved input a run consumed, for the run's audit page.</summary>
    public Task<List<PayrollRunInputSource>> GetInputSourcesAsync(
        Guid runId, CancellationToken cancellationToken = default) =>
        _context.PayrollRunInputSources.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .OrderBy(s => s.InputType)
            .ToListAsync(cancellationToken);

    /// <summary>Every payroll run that consumed a given input, which is the reverse question.</summary>
    public Task<List<PayrollRunInputSource>> GetRunsConsumingAsync(
        string inputType, Guid inputId, CancellationToken cancellationToken = default) =>
        _context.PayrollRunInputSources.AsNoTracking()
            .Where(s => s.InputType == inputType && s.InputId == inputId)
            .ToListAsync(cancellationToken);
}
