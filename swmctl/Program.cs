using System.IO.Pipes;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: swmctl <command> [args...]");
    Console.Error.WriteLine("examples: swmctl focus left | swmctl swap right | swmctl float toggle | swmctl list");
    return 2;
}

var line = string.Join(' ', args);

try
{
    using var client = new NamedPipeClientStream(".", "swm", PipeDirection.InOut);
    client.Connect(1000);
    using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
    using var reader = new StreamReader(client, leaveOpen: true);
    writer.WriteLine(line);
    var response = reader.ReadLine();
    if (response != null) Console.WriteLine(response);
    return 0;
}
catch (TimeoutException)
{
    Console.Error.WriteLine("swmctl: daemon not running (timeout connecting to \\\\.\\pipe\\swm)");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"swmctl: {ex.Message}");
    return 1;
}
