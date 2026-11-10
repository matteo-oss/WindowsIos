using System;
using Avalonia.Media;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.App.Views.Notifications;

/// <summary>
/// Data model for social platform notifications.
/// </summary>
public sealed record SocialNotificationModel
{
    public SocialPlatform Platform { get; init; } = SocialPlatform.WeChat;

    public string Sender { get; init; } = "张经理";

    public string Content { get; init; } = "项目下周一正式上线，请大家核对一下最新交付清单与测试报告。";

    public string Timestamp { get; init; } = "刚刚";

    public string? CustomSourceName { get; init; }

    public string DisplaySourceName => !string.IsNullOrWhiteSpace(CustomSourceName)
        ? CustomSourceName
        : ChatAppActivator.GetDisplayName(Platform);
}

/// <summary>
/// Platform visual definitions including brand color, accents, and SVG paths for icons.
/// </summary>
public static class SocialPlatformStyles
{
    // 微信绿
    public static readonly Color WeChatPrimary = Color.Parse("#07C160");
    public static readonly Color WeChatSecondary = Color.Parse("#059B4C");

    // 钉钉蓝
    public static readonly Color DingTalkPrimary = Color.Parse("#0089FF");
    public static readonly Color DingTalkSecondary = Color.Parse("#006ED6");

    // 飞书青蓝
    public static readonly Color FeishuPrimary = Color.Parse("#3370FF");
    public static readonly Color FeishuSecondary = Color.Parse("#00D6B9");

    // 企业微信深蓝
    public static readonly Color WeComPrimary = Color.Parse("#2984F8");
    public static readonly Color WeComSecondary = Color.Parse("#1A68D9");

    public static Color GetPrimaryColor(SocialPlatform platform) => platform switch
    {
        SocialPlatform.WeChat => WeChatPrimary,
        SocialPlatform.DingTalk => DingTalkPrimary,
        SocialPlatform.Feishu => FeishuPrimary,
        SocialPlatform.WeCom => WeComPrimary,
        _ => WeChatPrimary
    };

    public static Color GetSecondaryColor(SocialPlatform platform) => platform switch
    {
        SocialPlatform.WeChat => WeChatSecondary,
        SocialPlatform.DingTalk => DingTalkSecondary,
        SocialPlatform.Feishu => FeishuSecondary,
        SocialPlatform.WeCom => WeComSecondary,
        _ => WeChatSecondary
    };

    /// <summary>
    /// StreamGeometry path data for WeChat chat bubbles.
    /// </summary>
    public const string WeChatIconPath =
        "M18.5,4 C11.6,4 6,8.7 6,14.5 C6,17.8 7.8,20.7 10.6,22.6 L9.5,26 L13.4,24.3 C14.9,24.8 16.7,25 18.5,25 C19,25 19.5,25 20,24.9 C19.4,23.5 19,22 19,20.5 C19,15.2 24.4,11 31,11 C31.7,11 32.4,11.1 33,11.2 C31.5,7 25.5,4 18.5,4 Z " +
        "M14,10 C15.1,10 16,10.9 16,12 C16,13.1 15.1,14 14,14 C12.9,14 12,13.1 12,12 C12,10.9 12.9,10 14,10 Z " +
        "M23,10 C24.1,10 25,10.9 25,12 C25,13.1 24.1,14 23,14 C21.9,14 21,13.1 21,12 C21,10.9 21.9,10 23,10 Z " +
        "M31,13 C25.5,13 21,16.8 21,21.5 C21,24.2 22.4,26.6 24.7,28.2 L24,31 L27.2,29.6 C28.4,30 29.7,30.2 31,30.2 C36.5,30.2 41,26.4 41,21.5 C41,16.8 36.5,13 31,13 Z " +
        "M27.5,18 C28.3,18 29,18.7 29,19.5 C29,20.3 28.3,21 27.5,21 C26.7,21 26,20.3 26,19.5 C26,18.7 26.7,18 27.5,18 Z " +
        "M34.5,18 C35.3,18 36,18.7 36,19.5 C36,20.3 35.3,21 34.5,21 C33.7,21 33,20.3 33,19.5 C33,18.7 33.7,18 34.5,18 Z";

    /// <summary>
    /// StreamGeometry path data for DingTalk flying wing/lightning.
    /// </summary>
    public const string DingTalkIconPath =
        "M8,22 L38,10 L28,38 L22,27 L18,34 L17,25 Z M22,27 L34,14 L19,23 Z";

    /// <summary>
    /// StreamGeometry path data for Feishu flying bird.
    /// </summary>
    public const string FeishuIconPath =
        "M10,28 C12,18 22,8 36,6 C34,18 26,26 14,32 Z M20,24 C24,20 30,16 38,14 C32,24 24,32 16,38 Z";

    /// <summary>
    /// StreamGeometry path data for WeCom corporate chat badge.
    /// </summary>
    public const string WeComIconPath =
        "M14,6 C8.5,6 4,10.5 4,16 C4,19.2 5.5,22 8,23.8 L7,28 L11.8,26.2 C12.8,26.7 13.9,27 15,27 C15,26 15.2,25 15.5,24.1 C14.5,23.9 13.5,23.5 12.6,23 L9.5,24.2 L10.1,21.3 C8.2,19.8 7,17.5 7,15 C7,11.1 10.6,8 15,8 C19.4,8 23,11.1 23,15 C23,15.7 22.9,16.4 22.7,17.1 C24.2,17.5 25.5,18.3 26.5,19.4 C26.8,18 27,16.5 27,15 C27,10 21.2,6 14,6 Z " +
        "M26,18 C20.5,18 16,22 16,27 C16,29.8 17.5,32.3 19.8,33.8 L19,38 L23.2,36.4 C24.1,36.8 25,37 26,37 C31.5,37 36,33 36,27 C36,22 31.5,18 26,18 Z";

    public static string GetIconPath(SocialPlatform platform) => platform switch
    {
        SocialPlatform.WeChat => WeChatIconPath,
        SocialPlatform.DingTalk => DingTalkIconPath,
        SocialPlatform.Feishu => FeishuIconPath,
        SocialPlatform.WeCom => WeComIconPath,
        _ => WeChatIconPath
    };
}
