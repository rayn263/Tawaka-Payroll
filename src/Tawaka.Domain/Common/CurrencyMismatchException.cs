namespace Tawaka.Domain.Common;

/// <summary>
/// Thrown when an operation would combine amounts denominated in different currencies without an
/// explicit, recorded conversion. USD and ZiG are never implicitly interchangeable.
/// </summary>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(CurrencyCode left, CurrencyCode right)
        : base($"Cannot combine {left} and {right} without an explicit currency conversion. " +
               "Use an ExchangeRate and record the conversion provenance.")
    {
        Left = left;
        Right = right;
    }

    public CurrencyCode Left { get; }
    public CurrencyCode Right { get; }
}
