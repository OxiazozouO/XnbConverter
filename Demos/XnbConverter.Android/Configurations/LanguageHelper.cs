using Android.Content.Res;
using Java.Util;

namespace XnbConverter.Android.Configurations;

public static class LanguageHelper
{
    public static String[]? Locales;

    public static void Build(string language = "")
    {
        var res = Application.Context.Resources;
        if (res is null) return;
        Locales ??= res.Assets?.GetLocales();
        var locale = language == "" ? Locale.Default : new Locale(language);

        // 获取配置对象
        Configuration? configuration = res.Configuration;
        configuration?.SetLocale(locale);
        res.UpdateConfiguration(configuration, res.DisplayMetrics);
    }
}