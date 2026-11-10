using FluidBar.Platform.Windows.Notifications;
using Xunit;

namespace FluidBar.Platform.Tests;

public sealed class NotificationDatabaseReaderTests
{
    [Theory]
    [InlineData("Tencent.WeChat", "微信")]
    [InlineData("WeixinShare_fv0hcek9q9tbw!WeixinShare", "微信")]
    [InlineData("com.alibaba.DingTalk", "钉钉")]
    [InlineData("Bytedance.Feishu", "飞书")]
    [InlineData("Tencent.WXWork", "企业微信")]
    public void FriendlyAppName_MapsSupportedChatClients(string primaryId, string expected)
        => Assert.Equal(expected, NotificationDatabaseReader.FriendlyAppName(primaryId));

    [Fact]
    public void FriendlyAppName_PrioritizesWeComOverWeChat()
        => Assert.Equal("企业微信", NotificationDatabaseReader.FriendlyAppName("wework.wechat.bridge"));

    [Fact]
    public void FriendlyAppName_UsesReadablePrefixForOtherApps()
        => Assert.Equal("Microsoft.VisualStudioCode",
            NotificationDatabaseReader.FriendlyAppName("Microsoft.VisualStudioCode_8wekyb3d8bbwe!VSCode"));
}
