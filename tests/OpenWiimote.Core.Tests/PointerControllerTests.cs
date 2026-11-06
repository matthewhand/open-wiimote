using OpenWiimote.Core.Models;
using OpenWiimote.Core.Pointer;
using Xunit;
namespace OpenWiimote.Core.Tests;

public sealed class PointerControllerTests
{
    private static readonly CameraPoint[] Corners = [new(900,100), new(100,100), new(100,650), new(900,650)];
    [Fact]
    public void PerspectiveMappingHandlesMirroringUltrawideEdgesAndInvalidQuads()
    {
        Assert.True(PointerMath.TryTransform(Corners, out var map));
        double[] u = [.05,.95,.95,.05], v = [.05,.05,.95,.95];
        for (var n=0;n<4;n++)
        {
            Assert.True(PointerMath.TryMap(Corners[n], map, out var x, out var y));
            Assert.Equal(u[n],x,6); Assert.Equal(v[n],y,6);
        }
        Assert.True(PointerMath.TryMap(new(500,375),map,out var middleX,out var middleY));
        Assert.Equal(.5,middleX,6); Assert.Equal(.5,middleY,6);
        Assert.False(PointerMath.TryTransform([Corners[0],Corners[2],Corners[1],Corners[3]],out _));
        Assert.False(PointerMath.TryTransform([Corners[0],Corners[0],Corners[0],Corners[0]],out _));
        Assert.False(PointerMath.TryTransform([new(double.NaN,0),..Corners.Take(3)],out _));
        Assert.False(PointerMath.TryTransform([new(1024,0),..Corners.Take(3)],out _));
        Assert.False(PointerMath.TryTransform(null,out _));
        Assert.False(PointerMath.TryTransform([new(500,500),new(520,500),new(520,520),new(500,520)],out _));
        Assert.True(PointerMath.TryTransform([new(900,100),new(100,120),new(130,640),new(860,650)],out _));
    }
    [Fact]
    public void ExactlyTwoPlausibleLightsAreRequired()
    {
        var lights = Points(500,300);
        Assert.True(PointerMath.TryMidpoint(lights,out var midpoint)); Assert.Equal(new(500,300),midpoint);
        Assert.False(PointerMath.TryMidpoint(lights.Take(1).ToArray(),out _));
        Assert.False(PointerMath.TryMidpoint([..lights,new(){IsValid=true,X=700,Y=300}],out _));
        Assert.False(PointerMath.TryMidpoint([new(){IsValid=true,X=1024,Y=300},lights[0]],out _));
        Assert.False(PointerMath.TryMidpoint([lights[0],lights[0]],out _));
    }
    [Fact]
    public void OnlySelectedRemoteCanClickAndSwitchReleasesBeforeCreatingANewOutput()
    {
        using var f=new Fixture(); f.Select("one"); f.Sample(); f.Controller.Enable(true); f.Sample(); f.Sample(WiimoteButton.A);
        Assert.True(f.Backend.Last!.States.Last().Left);
        var old=f.Backend.Last;
        f.Controller.Update("two",f.Two,f.State(WiimoteButton.None));
        Assert.True(old.States.Last().Left);
        f.Select("two"); Assert.True(old.Disposed); Assert.False(old.States.Last().Left);
        f.Sample(WiimoteButton.A); f.Controller.Enable(true); f.Sample(WiimoteButton.A);
        Assert.False(f.Backend.Last!.States.Last().Left); // Held during handover: release before clicking.
        f.Sample(); f.Sample(WiimoteButton.A); Assert.True(f.Backend.Last.States.Last().Left);
    }
    [Fact]
    public void StaleIrCannotBeRefreshedByButtonsStatusOrAccelerometerReports()
    {
        using var f=new Fixture(); f.Select("one");f.Sample();f.Controller.Enable(true);f.Sample();f.Sample(WiimoteButton.A);
        var last=f.LastState;
        f.Clock.Advance(300);
        f.Controller.Update("one",f.One,last with {LastReport=f.Clock.GetUtcNow(),Buttons=WiimoteButton.A});
        f.Controller.Tick();Assert.False(f.Backend.Last!.States.Last().Left);Assert.False(f.Backend.Last.States.Last().Aiming);
        f.Sample(WiimoteButton.A);Assert.False(f.Backend.Last.States.Last().Left);
        f.Sample();f.Sample(WiimoteButton.A);Assert.True(f.Backend.Last.States.Last().Left);
    }
    [Fact]
    public void TrackingLossHomeDisconnectAndBackendFailureReleaseButtons()
    {
        using var f=new Fixture();f.Select("one");f.Sample();f.Controller.Enable(true);f.Sample();f.Sample(WiimoteButton.A|WiimoteButton.B);
        f.Controller.Update("one",f.One,f.State() with {IrPoints=[]});
        Assert.False(f.Backend.Last!.States.Last().Left); Assert.False(f.Backend.Last.States.Last().Right);
        f.Sample();f.Sample(WiimoteButton.A);f.Controller.Update("one",f.One,new(){Buttons=WiimoteButton.Home});
        Assert.True(f.Backend.Last.Disposed);Assert.False(f.Controller.Snapshot().Enabled);
        f.Sample();f.Controller.Enable(true);f.Sample();f.Backend.Last!.Fail=true;f.Sample(WiimoteButton.A);
        Assert.False(f.Controller.Snapshot().Enabled);Assert.True(f.Backend.Last.Disposed);
        f.Sample();f.Controller.Enable(true);f.Sample();f.Sample(WiimoteButton.A);f.Controller.Disconnected("one",f.One);
        Assert.True(f.Backend.Last!.Disposed);Assert.False(f.Controller.Snapshot().Enabled);
    }
    [Fact]
    public void ReplacementIgnoresOldReportsAndOldDisconnectsAndStartsDisabled()
    {
        using var f=new Fixture();f.Select("one");f.Sample();f.Controller.Enable(true);
        var replacement=new object();f.Controller.Connected("one",replacement);
        Assert.False(f.Controller.Snapshot().Enabled);
        f.Controller.Disconnected("one",f.One);f.Controller.Update("one",f.One,f.State(WiimoteButton.A));
        Assert.False(f.Controller.Snapshot().IrVisible);
        f.Controller.Update("one",replacement,f.State());Assert.True(f.Controller.Snapshot().IrVisible);
    }
    [Fact]
    public void FourStillTargetsPersistPerRemoteAndDisplayAndRejectMovingOrMissingSamples()
    {
        using var f=new Fixture();f.Select("one");f.Controller.BeginCalibration();
        f.Sample(WiimoteButton.A);Assert.Equal(0,f.Controller.Snapshot().Step);
        foreach(var corner in Corners)
        {
            for(var n=0;n<10;n++){f.Clock.Advance(40);f.Sample(x:(int)corner.X,y:(int)corner.Y);}
            Assert.True(f.Controller.Snapshot().Stable);
            f.Sample(WiimoteButton.A,(int)corner.X,(int)corner.Y);
        }
        Assert.False(f.Controller.Snapshot().Calibrating);Assert.True(f.Controller.Snapshot().Calibrated);
        Assert.NotNull(f.Saved);Assert.True(f.Saved!.Calibrations.ContainsKey("one|screen"));
        using var restarted=new PointerController(f.Backend,f.Saved,_=>{},f.Clock);
        Assert.False(restarted.Snapshot().Enabled);Assert.True(restarted.Snapshot().Calibrated);
        f.Select("two");Assert.True(f.Controller.Snapshot().Calibrated);
        f.Backend.Display=f.Backend.Display with{Width=3440};f.Controller.Tick();Assert.False(f.Controller.Snapshot().Calibrated);
    }
    [Fact]
    public void CancellationBadCornersSaveFailureAndLayoutChangesPreservePreviousCalibration()
    {
        using var f=new Fixture();f.Select("one");f.Controller.BeginCalibration();
        f.Controller.CancelCalibration();Assert.True(f.Controller.Snapshot().Calibrated);
        f.Controller.BeginCalibration();
        for(var target=0;target<4;target++){for(var n=0;n<10;n++){f.Clock.Advance(40);f.Sample();}f.Sample(WiimoteButton.A);}
        Assert.NotNull(f.Controller.Snapshot().Error);Assert.True(f.Controller.Snapshot().Calibrated);
        f.FailSave=true;f.Controller.BeginCalibration();
        foreach(var point in Corners){for(var n=0;n<10;n++){f.Clock.Advance(40);f.Sample(x:(int)point.X,y:(int)point.Y);}f.Sample(WiimoteButton.A,(int)point.X,(int)point.Y);}
        Assert.Contains("disk",f.Controller.Snapshot().Error);Assert.True(f.Controller.Snapshot().Calibrated);
        f.Controller.BeginCalibration();f.Backend.Display=f.Backend.Display with{X=-5120};f.Controller.Tick();Assert.False(f.Controller.Snapshot().Calibrating);
    }
    [Fact]
    public void CalibrationPreviewShowsAmbiguousDotsButExpiresAndCannotBeMutatedByCallers()
    {
        using var f = new Fixture(); f.Select("one"); f.Controller.BeginCalibration();
        var report = f.State() with { IrPoints = [..Points(500, 300), new() { IsValid = true, X = 700, Y = 500 }] };
        f.Controller.Update("one", f.One, report);
        var snapshot = f.Controller.Snapshot();
        Assert.Equal(3, snapshot.IrPoints.Count); Assert.False(snapshot.IrVisible);
        Assert.Null(f.Backend.Last); Assert.False(snapshot.Enabled);
        ((IrPoint[])snapshot.IrPoints)[0] = new() { IsValid = true, X = 1, Y = 1 };
        Assert.Equal(480, f.Controller.Snapshot().IrPoints[0].X);
        f.Clock.Advance(300);
        f.Controller.Update("one", f.One, report with { LastReport = f.Clock.GetUtcNow() });
        Assert.Empty(f.Controller.Snapshot().IrPoints); Assert.Null(f.Controller.Snapshot().LastIrReport);
        f.Select("two"); Assert.Empty(f.Controller.Snapshot().IrPoints);
    }
    private static IrPoint[] Points(int x,int y)=>[new(){IsValid=true,X=x-20,Y=y},new(){IsValid=true,X=x+20,Y=y}];
    private sealed class Clock:TimeProvider
    {
        private long _ticks=DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow()=>new(Interlocked.Read(ref _ticks),TimeSpan.Zero);
        public void Advance(int ms)=>Interlocked.Add(ref _ticks,TimeSpan.FromMilliseconds(ms).Ticks);
    }
    private sealed class Fixture:IDisposable
    {
        public Clock Clock {get;}=new(); public Backend Backend {get;}=new();
        public object One {get;}=new();public object Two {get;}=new();
        public PointerController Controller {get;}public PointerPreferences? Saved;public bool FailSave;
        public WiimoteState LastState {get;private set;}=new();
        private string _selected="one";private long _frame;
        public Fixture()
        {
            var preferences=new PointerPreferences{Calibrations=new(){{"one|screen",new(Backend.Display.Fingerprint,Corners)},{"two|screen",new(Backend.Display.Fingerprint,Corners)}}};
            Controller=new(Backend,preferences,next=>{if(FailSave)throw new IOException("disk full");Saved=next;},Clock);
            Controller.Connected("one",One);Controller.Connected("two",Two);
        }
        public void Select(string key){Controller.Select(key,"screen");_selected=key;}
        public WiimoteState State(WiimoteButton buttons=WiimoteButton.None,int x=500,int y=375)=>new(){Buttons=buttons,IrFrameNumber=++_frame,LastIrReport=Clock.GetUtcNow(),LastReport=Clock.GetUtcNow(),IrPoints=Points(x,y)};
        public void Sample(WiimoteButton buttons=WiimoteButton.None,int x=500,int y=375){LastState=State(buttons,x,y);Controller.Update(_selected,_selected=="one"?One:Two,LastState);}
        public void Dispose()=>Controller.Dispose();
    }
    private sealed class Backend:IPointerBackend
    {
        public PointerDisplay Display=new("screen","Ultrawide",0,0,5120,1440);public Output? Last;
        public IReadOnlyList<PointerDisplay> GetDisplays()=>[Display];
        public IPointerOutput Create(PointerDisplay display)=>Last=new();
    }
    private sealed class Output:IPointerOutput
    {
        public List<(int X,int Y,bool Left,bool Right,bool Aiming)> States=[];public bool Disposed,Fail;
        public void Move(int x,int y,bool left,bool right,bool aiming){if(Fail)throw new IOException("input failure");States.Add((x,y,left,right,aiming));}
        public void Dispose(){Disposed=true;}
    }
}
