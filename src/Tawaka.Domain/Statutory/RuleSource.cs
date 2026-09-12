namespace Tawaka.Domain.Statutory;

/// <summary>
/// Where a statutory rule's value came from. Every rule must be traceable to a document, and the
/// verification workflow records who confirmed it and when.
/// </summary>
public class RuleSource
{
    /// <summary>E.g. "ZIMRA public notice", "SI 393 of 1993 s.12", "Finance Act No. 2 of 2024".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>URL or document reference.</summary>
    public string? SourceReference { get; set; }

    /// <summary>Publication date of the source document.</summary>
    public DateOnly? SourceDate { get; set; }

    /// <summary>Who confirmed this rule against the official source.</summary>
    public string? VerifiedBy { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public override string ToString() =>
        SourceDate is null ? Source : $"{Source} ({SourceDate})";
}
