using Delegat.Infrastructure.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Tests;

public class LoginTicketProtectorTests
{
    private static LoginTicketProtector CreateProtector()
    {
        var provider = new EphemeralDataProtectionProvider();
        return new LoginTicketProtector(provider, new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public void Protect_RoundTripsUserIdAndRememberMe()
    {
        var tickets = CreateProtector();
        var token = tickets.Protect(42, rememberMe: true);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(tickets.TryUnprotect(token, out var payload));
        Assert.Equal(42, payload.UserId);
        Assert.True(payload.RememberMe);
    }

    [Fact]
    public void TryUnprotect_RejectsGarbage()
    {
        var tickets = CreateProtector();
        Assert.False(tickets.TryUnprotect("not-a-ticket", out _));
        Assert.False(tickets.TryUnprotect("", out _));
        Assert.False(tickets.TryUnprotect(null, out _));
    }

    [Fact]
    public void TryUnprotect_RejectsExpiredTicket()
    {
        var tickets = CreateProtector();
        var token = tickets.Protect(7, rememberMe: false, lifetime: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(30);

        Assert.False(tickets.TryUnprotect(token, out _));
    }

    [Fact]
    public void Tickets_FromDifferentProviders_DoNotUnprotect()
    {
        var a = CreateProtector();
        var b = CreateProtector();
        var token = a.Protect(1, false);

        Assert.False(b.TryUnprotect(token, out _));
    }

    [Fact]
    public void TryConsume_AllowsPendingTicketOnce()
    {
        var tickets = CreateProtector();
        var token = tickets.Protect(9, rememberMe: false);

        Assert.True(tickets.TryConsume(token));
        Assert.False(tickets.TryUnprotect(token, out _));
        Assert.False(tickets.TryConsume(token));
    }
}
