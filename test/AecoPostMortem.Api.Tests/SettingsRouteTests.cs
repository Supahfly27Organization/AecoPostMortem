using System.Net.Http.Json;
using AecoPostMortem.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// The Settings surface's read-only half (Part A): GET /api/settings serves the operator's
/// currently-resolved configuration — the store path, whether it exists and its size, the Copilot
/// source root and whether it was found, and the configured exclusion list — as real facts, never a
/// guessed or zero-filled placeholder.
/// </summary>
public sealed class SettingsRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Before_any_ingest_the_store_is_reported_as_not_existing()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var settings = await client.GetFromJsonAsync<SettingsEnvelope>(ApiHost.SettingsRoute, Cancellation);

            Assert.NotNull(settings);
            Assert.False(settings!.StoreExists);
            Assert.Equal(temporary.Store.FilePath, settings.StorePath);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task After_the_store_is_opened_it_is_reported_as_existing_with_a_real_size()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var settings = await client.GetFromJsonAsync<SettingsEnvelope>(ApiHost.SettingsRoute, Cancellation);

            Assert.NotNull(settings);
            Assert.True(settings!.StoreExists);
            Assert.True(settings.StoreSizeBytes > 0);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_missing_copilot_root_is_reported_as_not_found()
    {
        using var temporary = new TemporaryStore();
        var missingRoot = MissingCopilotRoot(temporary);

        await using var app = ApiHost.Build(temporary.Store, missingRoot, port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var settings = await client.GetFromJsonAsync<SettingsEnvelope>(ApiHost.SettingsRoute, Cancellation);

            Assert.NotNull(settings);
            Assert.False(settings!.CopilotSourceFound);
            Assert.Equal(missingRoot, settings.CopilotSourceRoot);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task No_exclusions_file_beside_the_store_reports_this_repositorys_own_root_as_the_default()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var settings = await client.GetFromJsonAsync<SettingsEnvelope>(ApiHost.SettingsRoute, Cancellation);

            // ExclusionListSource.Load's own documented default (FR-7): this product's own checkout
            // root, found by walking upward — never an empty list on a machine that has one.
            Assert.NotNull(settings);
            Assert.Single(settings!.ExcludedRoots);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>A store opened anywhere but FR-11's documented per-user location says so, rather
    /// than serving a bare path the operator has to recognise. Only `--store <path>` can produce
    /// this in a real run (`Cli/CLAUDE.md`), which is what the Settings page's own wording says.
    /// </summary>
    [Fact]
    public async Task A_store_away_from_the_documented_location_is_reported_as_not_the_default()
    {
        using var temporary = new TemporaryStore();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var settings = await client.GetFromJsonAsync<SettingsEnvelope>(ApiHost.SettingsRoute, Cancellation);

            Assert.NotNull(settings);
            Assert.False(settings!.StoreIsAtDefaultLocation);
            Assert.Equal(temporary.Store.FilePath, settings.StorePath);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The other half, and the one a real operator sees: the default store reports itself as
    /// the default. Built against <see cref="StoreLocation.Default"/> as a path only — the host is
    /// never started and nothing opens it, so this test never creates or touches the real store.
    /// </summary>
    [Fact]
    public void The_documented_per_user_location_is_reported_as_the_default()
    {
        var settings = ApiHost.GetSettings(
            new LocalStore(StoreLocation.Default), Path.Combine(Path.GetTempPath(), "no-such-copilot-root"));

        Assert.True(settings.StoreIsAtDefaultLocation);
        Assert.Equal(StoreLocation.Default, settings.StorePath);
    }

    static string MissingCopilotRoot(TemporaryStore temporary) =>
        Path.Combine(temporary.Folder, "no-such-copilot-root");

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
}
