using OpenWiimote.Core.Protocol;
using Xunit;
namespace OpenWiimote.Core.Tests;
public sealed class IrFreshnessTests
{
    [Fact]
    public async Task OnlyIrReportsAdvanceTheFrameMarkerIncludingStationaryCoordinates()
    {
        var device=new FakeWiimoteDevice();
        using var connection=WiimoteConnection.Open(device,new WiimoteWatcherOptions(),"test");
        connection.Start();
        var ir=new byte[18];ir[0]=0x33;ir[6]=100;ir[7]=100;ir[8]=0;ir[9]=200;ir[10]=100;ir[11]=0;
        device.Queue(ir);await Wait(()=>connection.Snapshot.IrFrameNumber==1);
        var first=connection.Snapshot;Assert.NotNull(first.LastIrReport);
        var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ReportReceived+=(_,report)=>{if(report.ReportId==0x30)arrived.TrySetResult();};
        device.Queue(0x20,0,0,0,0,0,100);device.Queue(0x30,0,0);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(first.LastIrReport,connection.Snapshot.LastIrReport);Assert.Equal(1,connection.Snapshot.IrFrameNumber);
        device.Queue(ir);await Wait(()=>connection.Snapshot.IrFrameNumber==2);
    }
    private static async Task Wait(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while(!condition())await Task.Delay(5,timeout.Token);
    }
}
