using EdgeLink;

// Disposable console fixture: no UI, credentials, input, audio or connection.
if (args.Length == 2 && args[0] == "--clean-legacy") {
    Updates.RemoveLegacyBackups(args[1]);
    return;
}
using var executable = File.OpenRead(Environment.ProcessPath!);
executable.Seek(-4, SeekOrigin.End);
byte[] trailer = new byte[4]; executable.ReadExactly(trailer);
string mode = System.Text.Encoding.ASCII.GetString(trailer);
if (mode == "NEW!") Updates.ReportReady(Environment.GetCommandLineArgs());
if (mode == "HANG") Thread.Sleep(60000);
