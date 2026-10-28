using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace GitHubActivityDashboard.Api.Services;

public sealed class InMemoryTicketStore(IMemoryCache memoryCache) : ITicketStore
{
    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        memoryCache.Set(key, ticket, GetCacheOptions(ticket));
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        memoryCache.Set(key, ticket, GetCacheOptions(ticket));
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        memoryCache.TryGetValue(key, out AuthenticationTicket? ticket);
        return Task.FromResult(ticket);
    }

    public Task RemoveAsync(string key)
    {
        memoryCache.Remove(key);
        return Task.CompletedTask;
    }

    private static MemoryCacheEntryOptions GetCacheOptions(AuthenticationTicket ticket)
    {
        var expiresAt = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8);
        return new MemoryCacheEntryOptions { AbsoluteExpiration = expiresAt };
    }
}
