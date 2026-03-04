using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiskChecker.Application.Services;

/// <summary>
/// Loads and persists SMTP settings.
/// </summary>
public class EmailSettingsService : IEmailSettingsService
{
    private readonly DiskCheckerDbContext _dbContext;
    private readonly IOptions<EmailSettings> _defaults;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailSettingsService"/> class.
    /// </summary>
    /// <param name="dbContext">Database context.</param>
    /// <param name="defaults">Default settings from configuration.</param>
    public EmailSettingsService(DiskCheckerDbContext dbContext, IOptions<EmailSettings> defaults)
    {
        _dbContext = dbContext;
        _defaults = defaults;
    }

    /// <inheritdoc />
    public async Task<EmailSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.EmailSettings.SingleOrDefaultAsync(cancellationToken);
        if (record != null)
        {
            return Map(record);
        }

        var fallback = _defaults.Value ?? new EmailSettings();
        await SaveAsync(fallback, cancellationToken);
        return fallback;
    }

    /// <inheritdoc />
    public async Task SaveAsync(EmailSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var record = await _dbContext.EmailSettings.SingleOrDefaultAsync(cancellationToken);
        if (record == null)
        {
            record = new EmailSettingsRecord { Id = Guid.NewGuid() };
            _dbContext.EmailSettings.Add(record);
        }

        record.Host = settings.Host;
        record.Port = settings.Port;
        record.UseSsl = settings.UseSsl;
        record.UserName = settings.UserName;
        record.Password = settings.Password;
        record.FromName = settings.FromName;
        record.FromAddress = settings.FromAddress;
        record.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static EmailSettings Map(EmailSettingsRecord record)
    {
        return new EmailSettings
        {
            Host = record.Host,
            Port = record.Port,
            UseSsl = record.UseSsl,
            UserName = record.UserName,
            Password = record.Password,
            FromName = record.FromName ?? "DiskChecker",
            FromAddress = record.FromAddress
        };
    }
}
