using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Api;

/// <summary>
/// The endpoint something calls before it calls anything else, and the two things it must not
/// become: a way to learn how a bridge is configured, and a way to learn anything about anybody.
/// <para>
/// The facts it answers are written out by hand below rather than read off the type. A list derived
/// from the type would agree with whatever the type happened to carry, including the day a field is
/// added that nobody argued for, and this is the one answer this plugin gives to a caller who has
/// not yet proven they can do anything.
/// </para>
/// </summary>
public class CapabilityEndpointTests
{
    /// <summary>
    /// Every fact this answer carries, with the type it carries it as. Four lines is the whole
    /// endpoint.
    /// </summary>
    /// <returns>One entry per fact.</returns>
    public static TheoryData<string, string> TheFactsTheAnswerCarries()
        => new TheoryData<string, string>
        {
            { "AcceptedKinds", "IReadOnlyList`1" },
            { "ApiVersion", "String" },
            { "AutomaticApproval", "Boolean" },
            { "BridgeConfigured", "Boolean" }
        };

    /// <summary>
    /// A fresh install answers, which is the condition this endpoint exists for: nothing has been
    /// configured, and a caller still learns what it can do here.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AFreshInstallIsDiscoverable()
    {
        var answer = await Asked(new PluginConfiguration(), BackendReachability.NotConfigured);

        Assert.Equal("v1", answer.ApiVersion, StringComparer.Ordinal);
        Assert.Equal([RequestedItemKind.Movie, RequestedItemKind.Series], answer.AcceptedKinds);
        Assert.False(answer.AutomaticApproval);
        Assert.False(answer.BridgeConfigured);
    }

    /// <summary>
    /// The version answered is the one the routes sit under, written here as the literal a caller
    /// reads rather than as the constant, so a version bump is a visible change in the suite as well
    /// as in the path.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheVersionAnsweredIsTheOneTheRoutesSitUnder()
    {
        var answer = await Asked(new PluginConfiguration(), BackendReachability.NotConfigured);

        Assert.Equal(RequestsControllerBase.VersionSegment, answer.ApiVersion, StringComparer.Ordinal);
        Assert.EndsWith("/" + answer.ApiVersion, RequestsControllerBase.RoutePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// A kind an operator switched off is not offered. A caller that shows a button for it produces
    /// a refusal the person reading it cannot act on, which is the whole reason this fact is here
    /// rather than left to the enumeration.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AKindThisInstallDoesNotAcceptIsNotOffered()
    {
        var settings = new PluginConfiguration { AcceptsSeries = false };

        var answer = await Asked(settings, BackendReachability.NotConfigured);

        Assert.Equal([RequestedItemKind.Movie], answer.AcceptedKinds);
    }

    /// <summary>
    /// An install that accepts nothing says so rather than falling back to everything. Whether such
    /// a configuration should be refused at all is #96; what this refuses is the endpoint quietly
    /// answering with the enumeration when the settings say no.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnInstallThatAcceptsNothingOffersNothing()
    {
        var settings = new PluginConfiguration { AcceptsMovies = false, AcceptsSeries = false };

        var answer = await Asked(settings, BackendReachability.NotConfigured);

        Assert.Empty(answer.AcceptedKinds);
    }

    /// <summary>
    /// A bridge that is configured is reported as configured whether or not it answered when it was
    /// asked. Those are different facts and only the first is this endpoint's: a caller cannot act
    /// on somebody else's service being down, and telling it so would be reporting the state of a
    /// system the person calling does not administer.
    /// </summary>
    /// <param name="reachability">What the bridge says about itself.</param>
    /// <param name="expected">Whether a bridge is reported.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData(BackendReachability.NotConfigured, false)]
    [InlineData(BackendReachability.Reachable, true)]
    [InlineData(BackendReachability.Unreachable, true)]
    [InlineData(BackendReachability.CredentialRefused, true)]
    [InlineData(BackendReachability.Incompatible, true)]
    public async Task ABridgeIsReportedWhenThereIsOneAndNeverHowItIsConfigured(
        BackendReachability reachability,
        bool expected)
    {
        var answer = await Asked(new PluginConfiguration(), reachability);

        Assert.Equal(expected, answer.BridgeConfigured);
    }

    /// <summary>
    /// The answer carries those four facts and no others, each as the type written down for it.
    /// <para>
    /// This is the leg that refuses the field added later. Every fact here is a version, a switch or
    /// the set of kinds, so there is nothing in this shape that could hold an address, a credential
    /// or an identifier of a person, and adding one is a red suite rather than a review somebody
    /// might not run.
    /// </para>
    /// </summary>
    [Fact]
    public void TheAnswerCarriesThoseFactsAndNothingElse()
    {
        var expected = TheFactsTheAnswerCarries()
            .Select(row => string.Concat((string)row[0], " ", (string)row[1]))
            .OrderBy(fact => fact, StringComparer.Ordinal)
            .ToArray();

        var carried = typeof(InstallCapabilities)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(fact => string.Concat(fact.Name, " ", fact.PropertyType.Name))
            .OrderBy(fact => fact, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, carried);
    }

    /// <summary>
    /// The endpoint needs no store, so nothing it answers can depend on one being readable. A
    /// capability answer that failed because the queue could not be read would tell a caller this
    /// plugin is absent on the one server where knowing otherwise matters most.
    /// </summary>
    [Fact]
    public void TheEndpointTakesNoStore()
    {
        var taken = typeof(CapabilitiesController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["IInstallSettings", "IRequestBackend"], taken);
    }

    /// <summary>
    /// Asks the endpoint and unwraps the answer.
    /// </summary>
    /// <param name="settings">What the install is set to.</param>
    /// <param name="reachability">What the bridge says about itself.</param>
    /// <returns>What came back.</returns>
    private static async Task<InstallCapabilities> Asked(
        PluginConfiguration settings,
        BackendReachability reachability)
    {
        var controller = new CapabilitiesController(
            new FakeInstallSettings(settings),
            new FakeRequestBackend(reachability));

        var came = await controller.CapabilitiesAsync(CancellationToken.None).ConfigureAwait(false);
        var ok = Assert.IsType<OkObjectResult>(came.Result);

        return Assert.IsType<InstallCapabilities>(ok.Value);
    }
}
