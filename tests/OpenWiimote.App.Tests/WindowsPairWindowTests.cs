using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenWiimote.Platform.Windows;
using Xunit;
namespace OpenWiimote.App.Tests;

public sealed class WindowsPairWindowTests
{
    private static WindowsBluetoothRemote Remote => new("00:11:22:33:44:55", "Nintendo RVL-CNT-01", "10:20:30:40:50:60", "Test adapter", false, false);
    [AvaloniaFact]
    public async Task OpeningSearchesAutomaticallyAndAnEmptyFirstPassIsRetriedWithoutPairing()
    {
        var searches=0; var pairs=0;
        var window=new WindowsPairWindow(_=>Task.FromResult(++searches==1?Array.Empty<WindowsBluetoothRemote>():new[]{Remote}),
            (_,_)=>{pairs++;return Task.FromResult(new WindowsPairResult(true,"paired"));},
            TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(10));
        try
        {
            window.Show();
            await Wait(()=>window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem is WindowsBluetoothRemote);
            Assert.Equal(2,searches);Assert.Equal(0,pairs);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Pair selected remote")).IsEnabled);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task ClosingCancelsAnActiveSearchInsteadOfLeavingBackgroundWork()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window=new WindowsPairWindow(async token=>
        {
            started.TrySetResult();
            try{await Task.Delay(Timeout.Infinite,token);}
            finally{cancelled.TrySetResult();}
            return [];
        },(_,_)=>throw new InvalidOperationException("Must not pair"));
        window.Show();await started.Task.WaitAsync(TimeSpan.FromSeconds(3));window.Close();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    [AvaloniaFact]
    public async Task SearchErrorsRemainVisibleAndDoNotPretendNoRemoteWasFound()
    {
        var window=new WindowsPairWindow(_=>throw new IOException("Bluetooth adapter is busy"),(_,_)=>throw new InvalidOperationException());
        try
        {
            window.Show();
            await Wait(()=>window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Bluetooth adapter is busy"));
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Pair selected remote")).IsEnabled);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task WindowsWizardCancelsAndWaitsForOurSearchBeforeLaunchingWithoutPairing()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searchFinished = false; var launches = 0; var pairs = 0;
        var window = new WindowsPairWindow(async token =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                cancelled.TrySetResult();
                await releaseSearch.Task;
                searchFinished = true;
            }
            return [];
        }, (_, _) => { pairs++; return Task.FromResult(new WindowsPairResult(true, "paired")); },
        openWizard: () => { Assert.True(searchFinished); launches++; });
        try
        {
            window.Show(); await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var wizard = Button(window, "Open Windows pairing wizard");
            Assert.True(wizard.IsEnabled);
            wizard.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, launches); Assert.False(wizard.IsEnabled);
            releaseSearch.TrySetResult();
            await Wait(() => launches == 1 && wizard.IsEnabled);
            Assert.Equal(0, pairs);
            Assert.True(Button(window, "Find Wiimotes").IsEnabled);
        }
        finally { releaseSearch.TrySetResult(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task WindowsWizardFailureIsVisibleAndAllowsRetry()
    {
        var window = new WindowsPairWindow(_ => Task.FromResult(Array.Empty<WindowsBluetoothRemote>()),
            (_, _) => throw new InvalidOperationException("Must not pair"),
            openWizard: () => throw new IOException("wizard unavailable"));
        try
        {
            window.Show();
            Button(window, "Open Windows pairing wizard").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Wait(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Could not open Windows pairing: wizard unavailable"));
            Assert.True(Button(window, "Open Windows pairing wizard").IsEnabled);
            Assert.True(Button(window, "Find Wiimotes").IsEnabled);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task WindowsWizardCannotInterruptActiveAuthentication()
    {
        var paired = new TaskCompletionSource<WindowsPairResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        var window = new WindowsPairWindow(_ => Task.FromResult(new[] { Remote }), (_, _) => paired.Task,
            openWizard: () => launches++);
        try
        {
            window.Show();
            Button(window, "Pair selected remote").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            var wizard = Button(window, "Open Windows pairing wizard");
            Assert.False(wizard.IsEnabled);
            wizard.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(0, launches);
            paired.TrySetResult(new WindowsPairResult(false, "test complete"));
            await Wait(() => wizard.IsEnabled);
        }
        finally { paired.TrySetResult(new WindowsPairResult(false, "cancelled")); window.Close(); }
    }
    private static Button Button(Window window, string content) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, content));
    private static async Task Wait(Func<bool> condition)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while(!condition())await Task.Delay(5,deadline.Token);
    }
}
