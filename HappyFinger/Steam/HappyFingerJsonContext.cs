/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyFinger.Steam;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RandomGameDetails))]
internal partial class HappyFingerJsonContext : JsonSerializerContext { }