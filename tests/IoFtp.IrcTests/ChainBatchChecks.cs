using System.Text;
using System.Reflection;
using IoFtp.Desktop.Services;

internal static class ChainBatchChecks
{
    public static async Task Run()
    {
        var accepted = new List<string>();
        await using var server = new VisionaryPipeServer(request => { accepted.Add(request.SrcSite + "->" + request.DstSite); return Task.FromResult<object>(new { ok = true }); });
        async Task<string> Send(string line)
        {
            using var stream = new MemoryStream();
            var input = Encoding.UTF8.GetBytes(line + "\n"); stream.Write(input); stream.Position = 0;
            var method = typeof(VisionaryPipeServer).GetMethod("HandleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.Invoke(server, [stream, CancellationToken.None, 0L])!;
            return Encoding.UTF8.GetString(stream.ToArray(), input.Length, (int)stream.Length - input.Length);
        }
        var response = await Send("CHAINS\tTV\tRelease-GROUP\tA;B,A;C,B;D\t1\t2");
        if (!response.StartsWith("OK RECEIVED 3") || !accepted.SequenceEqual(new[] { "A->B", "A->C", "B->D" })) throw new Exception("Chain batch lost a pair or acknowledgement");
        response = await Send("CHAINS\tTV\tRelease-GROUP\tA;B,invalid\t1\t2");
        if (!response.StartsWith("ERROR") || accepted.Count != 3) throw new Exception("Invalid batch must not partially dispatch");
        response = await Send("TRANSFER\tTV\tRelease-GROUP\tA\tE");
        if (!response.StartsWith("OK") || accepted.Last() != "A->E") throw new Exception("Legacy single transfer broken");
        Console.WriteLine("PASS: complete chain batch, invalid batch rejection, acknowledgement and legacy single transfer.");
    }
}
