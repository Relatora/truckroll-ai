using System.Text.Json;
using System.Text.Json.Serialization;

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>Deserialises API responses the way the web app sees them (camelCase, enums as names).</summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
