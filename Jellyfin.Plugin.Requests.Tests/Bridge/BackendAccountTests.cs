using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Requests.Bridge;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// Whose name a request carries on the external service, and what a person who is not on the
/// operator's table gets instead.
/// <para>
/// The two failures these legs stand against are opposite. One is a Jellyfin user's name arriving at
/// a system they never signed up to. The other is every request over there coming from one account,
/// so that side cannot tell who asked and nobody here notices, because the queue on this side is
/// still right.
/// </para>
/// </summary>
public class BackendAccountTests
{
    private static readonly Guid Anna = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bram = new Guid("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// A fresh install has mapped nobody. This is the shipping answer rather than a state on the way
    /// to one, and it is the reason the leg below is the ordinary case rather than an edge one.
    /// </summary>
    [Fact]
    public void AFreshInstallHasMappedNobody()
    {
        Assert.Equal(0, BackendAccounts.Empty.Count);
    }

    /// <summary>
    /// Somebody the operator has not mapped is attributed to the service's own account, which is a
    /// value that says so rather than an absence each caller would answer differently.
    /// </summary>
    [Fact]
    public void SomebodyTheOperatorHasNotMappedGoesUnderTheServiceAccount()
    {
        var account = BackendAccounts.Empty.For(Anna);

        Assert.Equal(BackendAccount.TheServiceAccount, account);
        Assert.False(account.CarriesWhoAsked);
        Assert.Null(account.Name);
    }

    /// <summary>
    /// Somebody the operator mapped carries the account the operator wrote, exactly as they wrote
    /// it. Nothing normalises it: an account name over there is that service's string and not this
    /// plugin's to tidy.
    /// </summary>
    [Fact]
    public void SomebodyTheOperatorMappedCarriesTheAccountTheOperatorWrote()
    {
        var accounts = new BackendAccounts(new Dictionary<Guid, string> { [Anna] = "anna.on.the.service" });

        var account = accounts.For(Anna);

        Assert.Equal("anna.on.the.service", account.Name);
        Assert.True(account.CarriesWhoAsked);
    }

    /// <summary>
    /// One person being mapped does not map the rest. Without this leg the two above pass for an
    /// implementation that answers with whichever row it read last.
    /// </summary>
    [Fact]
    public void MappingOnePersonLeavesEverybodyElseUnmapped()
    {
        var accounts = new BackendAccounts(new Dictionary<Guid, string> { [Anna] = "anna.on.the.service" });

        Assert.Equal(BackendAccount.TheServiceAccount, accounts.For(Bram));
    }

    /// <summary>
    /// Nothing is resolved from what a person is called. The lookup takes a user identifier and
    /// there is no other way in, so matching by name is a shape this type does not have rather than
    /// a behaviour somebody has to remember not to add. Two people with similar names would
    /// otherwise be attributed each other's requests, and the first sign of it is one of them seeing
    /// something that is not theirs.
    /// </summary>
    [Fact]
    public void NothingIsResolvedFromWhatAPersonIsCalled()
    {
        var lookups = typeof(BackendAccounts)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, nameof(BackendAccounts.For), StringComparison.Ordinal))
            .ToArray();

        var taken = lookups
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Single(lookups);
        Assert.Equal([nameof(Guid)], taken);
    }

    /// <summary>
    /// The account carries nothing but what the operator typed. This is the guard on the sentence in
    /// the documentation about what leaves the server: a field added here later that held a Jellyfin
    /// display name would send it to the service on the next submission and read as an improvement
    /// in the diff.
    /// </summary>
    [Fact]
    public void TheAccountCarriesNothingButWhatTheOperatorTyped()
    {
        var carried = typeof(BackendAccount)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => string.Concat(property.Name, " is ", property.PropertyType.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected = ["CarriesWhoAsked is Boolean", "Name is String"];

        Assert.Equal(expected, carried);
    }

    /// <summary>
    /// A row naming no user is refused. Honouring one would be an account that every unmapped person
    /// is attributed to, arriving through a row somebody left half filled in.
    /// </summary>
    [Fact]
    public void ARowNamingNoUserIsRefused()
    {
        var table = new Dictionary<Guid, string> { [Guid.Empty] = "somebody" };

        Assert.Throws<ArgumentException>(() => new BackendAccounts(table));
    }

    /// <summary>
    /// A row naming no account is refused. Removing the row is how a person is left unmapped, and a
    /// blank account is a row that was started rather than a decision to attribute nothing.
    /// </summary>
    /// <param name="blank">An account name that is not one.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARowNamingNoAccountIsRefused(string blank)
    {
        var table = new Dictionary<Guid, string> { [Anna] = blank };

        var refused = Assert.Throws<ArgumentException>(() => new BackendAccounts(table));

        Assert.Contains(Anna.ToString(), refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The table is copied rather than held. A caller that kept the dictionary it passed in could
    /// otherwise add a person to the mapping afterwards, and what the service is told about who
    /// asked would change under a submission already being made.
    /// </summary>
    [Fact]
    public void TheTableIsCopiedRatherThanHeld()
    {
        var table = new Dictionary<Guid, string> { [Anna] = "anna.on.the.service" };
        var accounts = new BackendAccounts(table);

        table[Bram] = "bram.on.the.service";

        Assert.Equal(BackendAccount.TheServiceAccount, accounts.For(Bram));
        Assert.Equal(1, accounts.Count);
    }
}
