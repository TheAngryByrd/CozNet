using Microsoft.Diagnostics.Tracing;
using System.Reflection;
using System.Text.Json;

using var source = new EventPipeEventSource(args[0]);
var events = new List<object>();
source.Dynamic.All += item =>
{
    if (item.ProviderName == "CozNet-SampledSignals" ||
        (item.ProviderName == "Microsoft-Windows-DotNETRuntime" && (int)item.ID is 1 or 2 or 3 or 7 or 8 or 9))
    {
        var payload = new Dictionary<string, object>();
        for (int i = 0; i < item.PayloadNames.Length; i++)
            payload[item.PayloadNames[i]] = item.PayloadValue(i);
        events.Add(new
        {
            provider = item.ProviderName, id = (int)item.ID, version = item.Version, name = item.EventName,
            milliseconds = item.TimeStampRelativeMSec, processId = item.ProcessID, threadId = item.ThreadID, payload
        });
    }
};
source.Process();
var assembly = typeof(EventPipeEventSource).Assembly;
File.WriteAllText(args[1], JsonSerializer.Serialize(new
{
    decoderVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    decoderRuntime = Environment.Version.ToString(),
    eventsLost = source.EventsLost,
    events
}, new JsonSerializerOptions { WriteIndented = true }));
