using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Stable, non-secret short ID used by the Code 128 label on a certificate.
/// DS = hash of a real hardware serial (portable between installations);
/// DL = local database card ID (only for disks without a reliable serial).
/// The barcode identifies the DISK CARD, never a specific test/certificate.
/// </summary>
public static class DiskBarcode
{
    private const ulong Modulus = 10_000_000_000_000_000UL;

    public static string? ForCard(DiskCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Build(card.SerialNumber, card.Id);
    }

    public static string? ForCertificate(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Build(certificate.SerialNumber, certificate.DiskCardId);
    }

    public static string? Build(string? serialNumber, int cardId)
    {
        if (HasHardwareSerial(serialNumber))
        {
            var normalized = serialNumber.Trim().ToUpperInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            ulong value = BinaryPrimitives.ReadUInt64BigEndian(hash) % Modulus;
            return "DS" + value.ToString("D16", CultureInfo.InvariantCulture);
        }

        return cardId > 0
            ? "DL" + cardId.ToString("D16", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Only an exact scanned value (or exact serial) may select a disk.</summary>
    public static bool Matches(DiskCard card, string? scannedValue)
    {
        if (string.IsNullOrWhiteSpace(scannedValue))
            return false;
        string value = scannedValue.Trim();
        if (string.Equals(ForCard(card), value, StringComparison.OrdinalIgnoreCase))
            return true;
        // Also allow typing/pasting a physical serial in the global search.
        return HasHardwareSerial(card.SerialNumber) &&
               string.Equals(card.SerialNumber.Trim(), value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHardwareSerial(string? serial) =>
        !string.IsNullOrWhiteSpace(serial) &&
        !serial.Trim().StartsWith("NOSN-", StringComparison.OrdinalIgnoreCase) &&
        !new[] { "N/A", "UNKNOWN", "NEZNÁMÉ", "NONE", "-", "0" }
            .Contains(serial.Trim(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<DiskCard> FindMatches(
        IEnumerable<DiskCard> cards, string? scannedValue)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Where(c => Matches(c, scannedValue)).ToList();
    }
}
