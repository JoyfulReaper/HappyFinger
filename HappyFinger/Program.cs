/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyFinger;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Happy Finger Server";
});

builder.Services
    .AddOptions<HappyFingerOptions>()
    .Bind(builder.Configuration.GetSection(HappyFingerOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65535, "Finger:Port must be between 1 and 65535.")
    .Validate(options => options.MaxConcurrentConnections > 0, "Finger:MaxConcurrentConnections must be positive.")
    .Validate(options => options.RequestTimeoutSeconds > 0, "Finger:RequestTimeoutSeconds must be positive.")
    .ValidateOnStart();

builder.Services
    .AddOptions<PlanFileOptions>()
    .Bind(builder.Configuration.GetSection(PlanFileOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Path), "PlanFile:Path must not be empty.")
    .Validate(options => options.MaxBytes > 0, "PlanFile:MaxBytes must be positive.")
    .Validate(
        options => options.MaxBytes <= PlanFileOptions.MaxAllowedBytes,
        $"PlanFile:MaxBytes must be less than or equal to {PlanFileOptions.MaxAllowedBytes}.")
    .ValidateOnStart();

builder.Services
    .AddOptions<RandomSteamGameOptions>()
    .Bind(builder.Configuration.GetSection(RandomSteamGameOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "RandomSteamGame:BaseUrl must not be empty.")
    .Validate(
        options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)),
        "RandomSteamGame:BaseUrl must be an absolute HTTP or HTTPS URI.")
    .Validate(options => options.TimeoutSeconds > 0, "RandomSteamGame:TimeoutSeconds must be positive.")
    .Validate(options => options.TimeoutSeconds <= 30, "RandomSteamGame:TimeoutSeconds must be 30 seconds or less.")
    .ValidateOnStart();

builder.Services.AddMissionControlClient(
    builder.Configuration.GetSection(
        MissionControlClientOptions.SectionName));

builder.Services.AddSingleton<IPlanFileReader, PlanFileReader>();
builder.Services.AddSingleton<IFingerResponseResolver, FingerResponseResolver>();
builder.Services.AddSingleton<IRandomSteamGameClient, RandomSteamGameClient>();
builder.Services.AddHttpClient(
    RandomSteamGameClient.HttpClientName,
    (serviceProvider, client) =>
    {
        RandomSteamGameOptions options =
            serviceProvider
                .GetRequiredService<IOptions<RandomSteamGameOptions>>()
                .Value;

        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HappyFinger/1.0");
    });
builder.Services.AddHostedService<FingerWorker>();

var host = builder.Build();
host.Run();
