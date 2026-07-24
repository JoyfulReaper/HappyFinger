/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyFinger.Events;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(FingerRequestCompletedEvent))]
[JsonSerializable(typeof(FingerServiceStartedEvent))]
internal sealed partial class FingerJsonContext : JsonSerializerContext;
