using System;
using FluidBar.Platform.Windows.Interop;
using Xunit;

namespace FluidBar.Platform.Tests;

public class ChatAppActivatorTests
{
    [Theory]
    [InlineData(SocialPlatform.WeChat, "微信")]
    [InlineData(SocialPlatform.DingTalk, "钉钉")]
    [InlineData(SocialPlatform.Feishu, "飞书")]
    [InlineData(SocialPlatform.WeCom, "企业微信")]
    public void GetDisplayName_ReturnsCorrectChineseNames(SocialPlatform platform, string expectedName)
    {
        var name = ChatAppActivator.GetDisplayName(platform);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void Activate_WhenAppNotRunning_ReturnsFriendlyPromptOrLaunch()
    {
        // Testing activation against all platforms returns non-null result and friendly message
        foreach (SocialPlatform platform in Enum.GetValues<SocialPlatform>())
        {
            var result = ChatAppActivator.Activate(platform);
            Assert.NotNull(result);
            Assert.Equal(platform, result.Platform);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));

            // Must produce friendly and descriptive message
            var appName = ChatAppActivator.GetDisplayName(platform);
            Assert.Contains(appName, result.Message);
        }
    }

    [Fact]
    public void ForceForegroundWindow_WithZeroHwnd_ReturnsFalseSafely()
    {
        var result = ChatAppActivator.ForceForegroundWindow(IntPtr.Zero);
        Assert.False(result);
    }
}
