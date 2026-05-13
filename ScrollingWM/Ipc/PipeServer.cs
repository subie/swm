using System.IO.Pipes;
using System.Threading.Channels;

namespace ScrollingWM.Ipc;

public sealed class PipeServer
{
    public const string PipeName = "swm";
    private readonly Channel<PipeCommand> _channel;
    private readonly CancellationTokenSource _cts = new();

    public PipeServer(Channel<PipeCommand> channel) { _channel = channel; }

    public Task RunAsync() => Task.Run(LoopAsync);

    public void Stop() => _cts.Cancel();

    private async Task LoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, leaveOpen: true);
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var tcs = new TaskCompletionSource<string>();
                await _channel.Writer.WriteAsync(new PipeCommand(line.Trim(), tcs), ct);
                var response = await tcs.Task.WaitAsync(ct);
                await writer.WriteLineAsync(response);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.Error.WriteLine($"pipe error: {ex.Message}"); }
        }
    }
}

public sealed record PipeCommand(string Line, TaskCompletionSource<string> Reply);
