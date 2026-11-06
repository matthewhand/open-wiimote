using System.Diagnostics;

namespace OpenWiimote.Core.Bluetooth;

internal sealed record BluetoothCommandResult(bool Success, string Output);

internal static class BluetoothCommand
{
    // No shell, no elevation, no locale-dependent prompts. Both the tool and its process
    // have a deadline; cancellation always reaps the child before returning.
    internal static async Task<BluetoothCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("bluetoothctl")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.Environment["LC_ALL"] = "C";
        start.Environment["LANG"] = "C";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return new(false, "");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return new(false, "");
            }
            await Task.WhenAll(output, error).ConfigureAwait(false);
            return new(process.ExitCode == 0, output.Result);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return new(false, "");
        }
    }
}
