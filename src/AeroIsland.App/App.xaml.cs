using System.IO.Pipes;
using System.IO;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using AeroIsland.Infrastructure;

namespace AeroIsland.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _serverCancellation;
    private MainWindow? _main;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\AeroIsland.SingleInstance", out var first);
        _ownsMutex = first;
        if (!first)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "AeroIsland.Open", PipeDirection.Out, PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(600);
                await client.WriteAsync(new byte[] { 1 });
            }
            catch (Exception ex) when (ex is IOException or TimeoutException) { }
            Shutdown();
            return;
        }

        var storage = new LocalStorage();
        var settings = await storage.LoadSettingsAsync();
        _main = new MainWindow(storage, settings);
        MainWindow = _main;
        _main.Show();
        _serverCancellation = new CancellationTokenSource();
        _ = ListenAsync(_serverCancellation.Token);
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream("AeroIsland.Open", PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                var byteBuffer = new byte[1];
                if (await server.ReadAsync(byteBuffer, token) == 1 && byteBuffer[0] == 1)
                    await Dispatcher.InvokeAsync(() => _main?.Open());
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(100, token); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
