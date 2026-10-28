using System.Security.Claims;
using GitHubActivityDashboard.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace GitHubActivityDashboard.Tests;

public sealed class InMemoryTicketStoreTests
{
    [Fact]
    public async Task StoresRenewsAndRemovesAuthenticationTickets()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new InMemoryTicketStore(cache);
        var originalTicket = CreateTicket("original");
        var key = await store.StoreAsync(originalTicket);

        Assert.Same(originalTicket, await store.RetrieveAsync(key));

        var renewedTicket = CreateTicket("renewed");
        await store.RenewAsync(key, renewedTicket);
        Assert.Same(renewedTicket, await store.RetrieveAsync(key));

        await store.RemoveAsync(key);
        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task DoesNotRetrieveExpiredTickets()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new InMemoryTicketStore(cache);
        var ticket = CreateTicket("expired");
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var key = await store.StoreAsync(ticket);

        Assert.Null(await store.RetrieveAsync(key));
    }

    private static AuthenticationTicket CreateTicket(string name)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test");
        return new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties(), "test");
    }
}
