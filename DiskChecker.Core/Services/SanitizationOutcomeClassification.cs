using DiskChecker.Core.Interfaces;

namespace DiskChecker.Core.Services;

/// <summary>
/// Distinguish explicit user cancellation from a physical media error.
/// Both database and durable recovery journal use this classifier so a
/// recovered record cannot disagree with the originally saved session.
/// </summary>
public static class SanitizationOutcomeClassification
{
    /// <summary>
    /// A failed result is only treated as cancelled when no explicit hardware
    /// error details exist and the returned message indicates cancellation.
    /// </summary>
    public static bool IsUserCancelled(SanitizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Success || result.ErrorDetails.Count > 0)
            return false;

        var message = result.ErrorMessage ?? string.Empty;
        return message.Contains("cancel", StringComparison.OrdinalIgnoreCase)
               || message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("zrušen", StringComparison.OrdinalIgnoreCase)
               || message.Contains("zrusen", StringComparison.OrdinalIgnoreCase)
               || message.Contains("uživatelem", StringComparison.OrdinalIgnoreCase)
               || message.Contains("uzivatelem", StringComparison.OrdinalIgnoreCase);
    }
}
