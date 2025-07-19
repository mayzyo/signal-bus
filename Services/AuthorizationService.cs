using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SignalBus.Services;

public interface IAuthorizationService
{
    bool IsAuthorized(string value);
}

public class AuthorizationService : IAuthorizationService
{
    private readonly ILogger<AuthorizationService> _logger;
    private readonly HashSet<string> _whitelist;

    public AuthorizationService(IConfiguration configuration, ILogger<AuthorizationService> logger)
    {
        _logger = logger;
        
        // Get whitelist from environment variable (comma-separated values)
        var whitelistString = configuration["AUTHORIZATION_WHITELIST"];
        
        if (string.IsNullOrEmpty(whitelistString))
        {
            _logger.LogWarning("AUTHORIZATION_WHITELIST environment variable is not configured. All authorization checks will fail.");
            _whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // Split by comma and trim whitespace, use case-insensitive comparison
            _whitelist = new HashSet<string>(
                whitelistString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(item => item.Trim()),
                StringComparer.OrdinalIgnoreCase
            );
            
            _logger.LogInformation("Loaded {Count} items into authorization whitelist", _whitelist.Count);
        }
    }

    public bool IsAuthorized(string value)
    {
        var isAuthorised = _whitelist.Contains(value.Trim());
        return isAuthorised;
    }
}
