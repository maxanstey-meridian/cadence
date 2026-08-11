using System.Text.Json.Serialization;

namespace Cadence;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunStatus
{
    Running,
    Ready,
    WaitingForHuman,
    Failed,
    Faulted,
    Cancelled,
}
