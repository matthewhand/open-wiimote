using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenWiimote.App.Controls;
using OpenWiimote.Core.Models;
using Xunit;

namespace OpenWiimote.App.Tests;

/// <summary>
/// Draws the extension avatar on the headless platform, so the vector model is exercised
/// for every extension type even without a remote in front of the machine.
/// </summary>
public sealed class ExtensionAvatarTests
{
    [AvaloniaTheory]
    [InlineData(WiimoteExtensionType.None)]
    [InlineData(WiimoteExtensionType.Nunchuk)]
    [InlineData(WiimoteExtensionType.ClassicController)]
    [InlineData(WiimoteExtensionType.ClassicControllerPro)]
    [InlineData(WiimoteExtensionType.MotionPlus)]
    [InlineData(WiimoteExtensionType.Unknown)]
    public void TheAvatarDrawsEveryExtensionType(WiimoteExtensionType type)
    {
        var avatar = new ExtensionAvatar { Width = 320, Height = 190 };
        avatar.SetState(new WiimoteState
        {
            ExtensionConnected = type is not WiimoteExtensionType.None,
            ExtensionType = type,
            Nunchuk = type == WiimoteExtensionType.Nunchuk
                ? new NunchukState { StickX = 0.5f, ButtonC = true, ButtonZ = true }
                : null,
            ClassicController = type is WiimoteExtensionType.ClassicController or WiimoteExtensionType.ClassicControllerPro
                ? new ClassicControllerState
                {
                    ButtonA = true,
                    DPadUp = true,
                    LeftTrigger = 0.5f,
                    LeftStickX = 0.4f
                }
                : null
        });

        var window = new Window
        {
            Width = 320,
            Height = 190,
            Content = avatar
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            using var bitmap = new RenderTargetBitmap(new PixelSize(320, 190), new Vector(96, 96));
            bitmap.Render(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
