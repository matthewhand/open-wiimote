using System.Runtime.InteropServices;
using OpenWiimote.Core.Pointer;
using OpenWiimote.Platform.Windows;
using Xunit;
namespace OpenWiimote.Core.Tests;

public sealed class WindowsPointerTests
{
    [Fact]
    public void NativeInputLayoutMatchesWindowsAndUltrawideUsesSelectedMonitorWithNegativeCoordinates()
    {
        Assert.Equal(IntPtr.Size==8?40:28,Marshal.SizeOf<WindowsPointerBackend.NativeInput>());
        var target=new PointerDisplay("wide","wide",-5120,0,5120,1440);
        var desktop=new PointerDisplay("all","all",-5120,0,7680,2160);
        Assert.Equal((0,0),WindowsPointerOutput.ToVirtualAxes(0,0,target,desktop));
        var far=WindowsPointerOutput.ToVirtualAxes(65535,65535,target,desktop);
        Assert.InRange(far.X,43680,43695);Assert.InRange(far.Y,43670,43690);
    }
    [Fact]
    public void MouseTransitionsAreEdgesAndLossOfAimReleasesWithoutMoving()
    {
        var screen=new PointerDisplay("one","one",0,0,5120,1440);
        var events=new List<WindowsPointerBackend.NativeInput>();
        using var output=new WindowsPointerOutput(screen,()=>[screen],()=>screen,inputs=>events.AddRange(inputs));
        output.Move(100,200,true,false,true);output.Move(110,210,true,false,true);output.Move(0,0,false,false,false);
        Assert.True((events[0].Data.Mouse.Flags&2)!=0);
        Assert.Equal(0u,events[1].Data.Mouse.Flags&2);
        Assert.Equal(4u,events[2].Data.Mouse.Flags);Assert.Equal(0,events[2].Data.Mouse.X);
        output.Dispose();Assert.Equal(3,events.Count);
    }
    [Fact]
    public void PartialSendFailureStillReleasesOwnedButtonsAndDisplayLossCannotBlockRelease()
    {
        var screen=new PointerDisplay("one","one",0,0,5120,1440);var available=true;var calls=0;uint last=0;
        using var output=new WindowsPointerOutput(screen,()=>available?[screen]:[],()=>screen,inputs=>
        {
            last=inputs[0].Data.Mouse.Flags;if(++calls==1)throw new IOException("partial insertion");
        });
        Assert.Throws<IOException>(()=>output.Move(100,200,true,true,true));
        available=false;output.Dispose();Assert.Equal(4u|16u,last);
    }
}
