using System.Threading;

namespace Rdx.Services;

/// <summary>
/// Bloqueador simples em 2 camadas, sem frescura:
/// 1) rede: cancela requests para dominios/padroes conhecidos de ads/trackers.
/// 2) cosmetico: injeta CSS que esconde banners, popups e placeholders de anuncio.
/// </summary>
public static class AdBlocker
{
    public static bool Enabled { get; set; } = true;

    private static long _blockedCount;
    public static long BlockedCount => Interlocked.Read(ref _blockedCount);

    public static void ResetCounter() => Interlocked.Exchange(ref _blockedCount, 0);

    internal static void Hit() => Interlocked.Increment(ref _blockedCount);

    // Lista compacta inspirada no EasyList/EasyPrivacy. Suficiente p/ ~90% dos ads comuns.
    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "googletagservices.com", "googletagmanager.com", "adservice.google.com",
        "ads.youtube.com", "ads.yahoo.com", "advertising.com",
        "adnxs.com", "adsrvr.org", "criteo.com", "criteo.net",
        "outbrain.com", "taboola.com", "revcontent.com", "mgid.com",
        "amazon-adsystem.com", "aax.amazon-adsystem.com",
        "moatads.com", "scorecardresearch.com", "hotjar.com",
        "fullstory.com", "segment.io", "mixpanel.com",
        "facebook.net", "connect.facebook.net", "ads.facebook.com",
        "ads.twitter.com", "static.ads-twitter.com", "ads.linkedin.com",
        "ads.pinterest.com", "ads.tiktok.com",
        "popads.net", "popcash.net", "adcash.com", "exoclick.com",
        "juicyads.com", "trafficjunky.net", "adsterra.com",
        "propellerads.com", "hilltopads.net", "clickadu.com",
        "adcolony.com", "applovin.com", "ironsrc.com", "unityads.com",
        "chartboost.com", "vungle.com", "inmobi.com",
        "pubmatic.com", "rubiconproject.com", "openx.net", "openx.com",
        "smartadserver.com", "adform.net", "adform.com",
        "media.net", "yieldmo.com", "sharethrough.com",
        "quantserve.com", "crashlytics.com", "appsflyer.com",
        "branch.io", "adjust.com", "kochava.com",
        "doubleverify.com", "iasds01.com",
        "2mdn.net", "google-analytics.com", "googletagmanager",
        "adservice", "adbrite.com", "bidbarrel.com",
    };

    // Padrões de URL tipicos de anuncio (quando o host é generico/CDN).
    private static readonly string[] BlockedPatterns =
    {
        "/ads/", "/ad_", "/ad-", "_ad.", ".ad.", "/banner",
        "banner_ad", "/popunder", "/popup-ad", "/pagead/",
        "googlesyndication", "doubleclick", "/prebid", "/gampad",
        "ads.js", "advert", "/sponsor", "outbrain", "taboola",
        "criteo", "moatads", "amazon-adsystem", "adservice",
        "tracking.js", "fbevents.js", "hotjar", "fullstory",
    };

    public static bool ShouldBlock(string? url)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(url))
            return false;

        var lower = url.ToLowerInvariant();

        // 1) host exato ou subdominio de host bloqueado
        string host = "";
        try { host = new Uri(url).Host.ToLowerInvariant(); }
        catch { host = lower; }

        foreach (var h in BlockedHosts)
        {
            if (host == h || host.EndsWith("." + h))
                return true;
        }

        // 2) padrao de path/query
        foreach (var p in BlockedPatterns)
        {
            if (lower.Contains(p))
                return true;
        }

        return false;
    }

    /// <summary>CSS cosmetico injetado apos cada navegacao completa.</summary>
    public const string CosmeticCss =
        "[id*='ads'],[id*='Ads'],[class*='ads-'],[class*='-ads'],[class*='_ad_']," +
        "[id*='sponsor'],[class*='sponsor'],[id*='banner-ad'],[class*='banner-ad']," +
        "[id*='popunder'],[id*='popup-ad'],[class*='taboola'],[class*='outbrain']," +
        "[id*='criteo'],[class*='criteo'],iframe[src*='doubleclick'],iframe[src*='googlesyndication']," +
        "iframe[src*='adservice'],iframe[src*='amazon-adsystem'],div[data-ad],div[data-ads]," +
        "ins.adsbygoogle,div[id^='div-gpt-ad']" +
        "{display:none!important;visibility:hidden!important;height:0!important;}";

    public static string CosmeticJs =>
        "(function(){var s=document.getElementById('rdx-adblock');if(!s){s=document.createElement('style');" +
        "s.id='rdx-adblock';s.textContent=\"" + CosmeticCss.Replace("\"", "\\\"") + "\";" +
        "(document.head||document.documentElement).appendChild(s);}})();";
}
