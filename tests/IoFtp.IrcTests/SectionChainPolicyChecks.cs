using System.Reflection;
using System.Text.Json;
using System.Linq.Expressions;
using System.Collections.Concurrent;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;

internal static class SectionChainPolicyChecks
{
 public static async Task Run()
 {
var assembly = typeof(SectionChainPolicy).Assembly;
var policy = assembly.GetType("IoFtp.Desktop.Services.SectionChainPolicy")!;
var sectionType = assembly.GetType("IoFtp.Desktop.Services.SectionDefinition")!;
const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
ConnectionProfile Site(string name, int speed) => new(Guid.NewGuid(), name, "example.invalid", 21, "tester", TransferProtocol.Ftp, Options: new SiteOptions(LinkSpeedGbps: speed));
bool Allows(ConnectionProfile a, ConnectionProfile b) => (bool)policy.GetMethod("CanRoute", flags)!.Invoke(null, [a, b])!;
object Section(ConnectionProfile[] sites, string[] active) => Activator.CreateInstance(sectionType, ["MOVIES", sites.ToDictionary(p => p.Name, _ => "/MOVIES"), 0, "", "", Enum.ToObject(assembly.GetType("IoFtp.Desktop.Services.SectionValidationMode")!, 0), active])!;
string[][] Plan(object section, ConnectionProfile[] sites, string[][] incoming) => (string[][])policy.GetMethod("Plan", flags)!.Invoke(null, [section, sites, incoming])!;
var fast = Site("FAST", 10); var fast2 = Site("FAST2", 10); var slow = Site("SLOW", 1); var slow2 = Site("SLOW2", 1); var unknown = Site("UNKNOWN", 0);
Check(Allows(fast, fast2), "10->10 must work"); Check(Allows(fast, slow), "10->1 must work");
Check(Allows(slow, slow2), "1->1 must work"); Check(!Allows(slow, fast), "1->10 must be blocked");
Check(!Allows(unknown, fast) && !Allows(fast, unknown), "Unknown speed must be excluded");
Check(!Allows(fast, fast), "Self route must be excluded");
Check(!Allows(fast with { Options = fast.EffectiveOptions with { AllowDownload = false } }, slow), "Download permission must apply");
Check(!Allows(fast, slow with { Options = slow.EffectiveOptions with { AllowUpload = false } }), "Upload permission must apply");
Check(!Allows(fast with { Options = fast.EffectiveOptions with { BlockTransfersTo = "slow" } }, slow), "Blocked target must apply");
Check(!Allows(fast, slow with { Options = slow.EffectiveOptions with { BlockTransfersFrom = "fast" } }), "Blocked source must apply");
var sites = new[] { fast, fast2, slow, slow2, unknown }.Select(p => p with { Description = "Shared description" }).ToArray();
var section = Section(sites, sites.Select(p => p.Name).ToArray());
var rejected = new List<string>();
string[][] Diagnose(object config, string[][] incoming) => (string[][])policy.GetMethod("PlanWithDiagnostics", flags)!.Invoke(null,
    [config, sites, incoming, (Action<string>)rejected.Add])!;
Diagnose(section, [[fast.Name, unknown.Name], [slow.Name, fast.Name]]);
Check(rejected.Count == 2 && rejected.Any(message => message.Contains("speed class")) && rejected.Any(message => message.Contains("1 Gbit source to 10 Gbit")),
    "Filtered incoming routes must report speed classification and slow-to-fast reasons");
rejected.Clear();
Diagnose(Section(sites, [fast.Name]), [[fast.Name, fast2.Name]]);
Check(rejected.Single().Contains("Trading active is off"), "Inactive destination needs an explicit diagnostic");
var plan = Plan(section, sites, [[fast2.Name, slow.Name, "2", "3"], [slow.Name, fast.Name]]);
Check(plan[0].SequenceEqual(new[] { fast2.Name, slow.Name, "2", "3" }), "Visionary pair order and live slots must be preserved");
Check(plan.Length == 8, "Four classified participants must produce all eight permitted directed routes");
Check(plan.All(pair => pair[0] != "UNKNOWN" && pair[1] != "UNKNOWN" && !(pair[0].StartsWith("SLOW") && pair[1].StartsWith("FAST"))), "No unknown or 1->10 alternate routes");
var inactive = Plan(Section(sites, [fast.Name, slow.Name]), sites, []);
Check(inactive.Length == 1 && inactive[0].SequenceEqual(new[] { fast.Name, slow.Name }), "Inactive participants must never enter expanded chains");
var noPaths = JsonSerializer.Serialize(section, sectionType).Replace("\"/MOVIES\"", "\"\"");
Check(Plan(JsonSerializer.Deserialize(noPaths, sectionType)!, sites, []).Length == 0, "Missing section paths must exclude participants");
var many = Enumerable.Range(0, 40).Select(i => Site($"SITE{i:D2}", i < 20 ? 10 : 1)).ToArray();
var large = Plan(Section(many, many.Select(p => p.Name).ToArray()), many, [[many[0].Name, many[1].Name]]);
Check(large.Length == 1160 && large.Select(p => p[1]).Distinct().Count() == 40, "All 40 destinations must participate without forbidden slow->fast routes");
var optionsJson = JsonSerializer.Serialize(new SiteOptions(LinkSpeedGbps: 10));
Check(JsonSerializer.Deserialize<SiteOptions>(optionsJson)!.LinkSpeedGbps == 10, "Speed class must survive serialization");
var fixture = Path.Combine(Path.GetTempPath(), "chain-profile-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var storeType = assembly.GetType("IoFtp.Desktop.Services.ProfileStore")!;
var store = storeType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(string)], null)!.Invoke([fixture]);
storeType.GetMethod("Save")!.Invoke(store, [sites]);
var loaded = (IReadOnlyList<ConnectionProfile>)storeType.GetMethod("Load")!.Invoke(store, null)!;
Check(loaded.All(p => p.EffectiveOptions.LinkSpeedGbps == sites.Single(s => s.Id == p.Id).EffectiveOptions.LinkSpeedGbps), "Manual speed classes must survive actual ProfileStore INI save/load");
Check(loaded.Count == 5 && loaded.All(p => p.Description == "Shared description"), "Shared descriptions must save/load without merging distinct site names");
var settingsType = assembly.GetType("IoFtp.Desktop.Models.GlobalSettings")!;
var settings = JsonSerializer.Deserialize("{\"VisionarySectionChains\":true}", settingsType)!;
Check((bool)settingsType.GetProperty("VisionarySectionChains")!.GetValue(settings)!, "Global mode must survive settings serialization");
var sectionJson = JsonSerializer.Serialize(section, sectionType);
Check(Plan(JsonSerializer.Deserialize(sectionJson, sectionType)!, sites, []).Length == 8, "Trading-active membership must survive serialization");
var pipeType = assembly.GetType("IoFtp.Desktop.Services.VisionaryPipeServer")!;
var constructor = pipeType.GetConstructors().Single();
Delegate Adapt(Type signature, string method) {
    var target = typeof(PipelineProbe).GetMethod(method)!;
    var parameters = signature.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
    var arguments = parameters.Select((p, i) => Expression.Convert(p, target.GetParameters()[i].ParameterType));
    return Expression.Lambda(signature, Expression.Call(target, arguments), parameters).Compile();
}
var parameters = constructor.GetParameters();
var byName = sites.ToDictionary(p => p.Name);
var receiver = constructor.Invoke([Adapt(parameters[0].ParameterType, "Start"), null, Adapt(parameters[2].ParameterType, "Step"), null,
    1, 1, null, null, null, (Func<bool>)(() => true),
    (Func<string, string[][], string[][]>)((_, incoming) => Plan(section, sites, incoming)),
    (Func<string, string, string, bool>)((_, a, b) => Allows(byName[a], byName[b]))]);
await using (var running = (IAsyncDisposable)receiver) {
    var ack = await (Task<string>)pipeType.GetMethod("SubmitChainsAsync")!.Invoke(receiver, ["MOVIES", "Local.Policy.Test-GROUP", "SLOW;FAST"] )!;
    Check(ack.StartsWith("OK RECEIVED 8"), "Pipeline must replace rejected input with all permitted section routes");
    await PipelineProbe.AllRoutes.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(PipelineProbe.Routes.Count == 8 && !PipelineProbe.Routes.Keys.Any(r => r.StartsWith("SLOW->FAST") || r.StartsWith("SLOW2->FAST")), "No forbidden alternate source may reach race scheduling");
}
Console.WriteLine($"PASS: {checks} section-chain policy checks, including all direction combinations, active membership, live slots, 40 destinations, blocks and serialization. No real sites contacted.");

 }
}
public static class PipelineProbe {
 public static ConcurrentDictionary<string, bool> Routes = new();
 public static TaskCompletionSource AllRoutes = new(TaskCreationOptions.RunContinuationsAsynchronously);
 public static Task<object> Start(object request) => Task.FromResult<object>(new { ok = true });
 public static Task<bool> Step(object request, object state, CancellationToken token) {
    token.ThrowIfCancellationRequested();
    var type = request.GetType();
    var route = type.GetProperty("SrcSite")!.GetValue(request) + "->" + type.GetProperty("DstSite")!.GetValue(request);
    Routes[route] = true; if (Routes.Count == 8) AllRoutes.TrySetResult();
    return Task.FromResult(false);
 }
}
