using System.Text.Json.Serialization;
using OpenFortiClient.Core.Notifications;
using OpenFortiClient.Core.Profiles;
using OpenFortiClient.Core.Settings;

namespace OpenFortiClient.Core;

/// <summary>
/// Source-generated JSON metadata, so serialization keeps working when the app is trimmed.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<VpnProfile>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<NotificationEntry>))]
[JsonSerializable(typeof(ConfigurationBackup))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
