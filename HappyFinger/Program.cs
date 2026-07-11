/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyFinger;
using HappyGopher;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Happy Gopher Server";
});

builder.Services
    .AddOptions<HappyFingerOptions>()
    .Bind(builder.Configuration.GetSection(HappyFingerOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65535, "Finger:Port must be between 1 and 65535.")
    .Validate(options => options.MaxConcurrentConnections > 0, "Finger:MaxConcurrentConnections must be positive.")
    .Validate(options => options.RequestTimeoutSeconds > 0, "Finger:RequestTimeoutSeconds must be positive.")
    .ValidateOnStart();

builder.Services.AddHostedService<FingerWorker>();

var host = builder.Build();
host.Run();
