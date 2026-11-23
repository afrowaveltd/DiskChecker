using System;
using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Safe rules for an explicitly selected 2-3 part disk report.
/// The parts remain independent: never average grades, metrics or SMART data.
/// </summary>
public static class CombinedCertificatePolicy
{
    public static void Validate(IReadOnlyList<DiskCertificate> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        if (certificates.Count is < 2 or > 3)
            throw new ArgumentException("Vyberte dva nebo tři certifikáty.", nameof(certificates));

        int cardId = certificates[0]?.DiskCardId ?? 0;
        if (cardId <= 0 || certificates.Any(c => c is null || c.DiskCardId != cardId))
            throw new ArgumentException("Všechny certifikáty musí patřit ke stejné uložené kartě disku.", nameof(certificates));

        if (certificates.Any(c => c.TestSessionId <= 0) ||
            certificates.Select(c => c.TestSessionId).Distinct().Count() != certificates.Count)
            throw new ArgumentException("Každý certifikát musí pocházet z jiného existujícího testu.", nameof(certificates));

        if (certificates.Select(c => Category(c.TestType)).Distinct(StringComparer.Ordinal).Count() != certificates.Count)
            throw new ArgumentException("Vyberte různé druhy testů (například SEEK, sanitizaci a SMART).", nameof(certificates));
    }

    public static string Category(string? testType)
    {
        string value = (testType ?? "").Trim().ToUpperInvariant();
        if (value.StartsWith("SMART", StringComparison.Ordinal) || value.Contains("SMART-ONLY", StringComparison.Ordinal))
            return "SMART";
        if (value is "SEEK" or "SEEKTEST" || value.Contains("SEEK TEST", StringComparison.Ordinal))
            return "SEEK";
        if (value is "SANITIZATION" or "SANITIZACE")
            return "SANITIZATION";
        return value;
    }
}
