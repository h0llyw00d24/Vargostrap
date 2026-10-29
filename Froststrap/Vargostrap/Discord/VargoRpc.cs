// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using DiscordRPC;
using RichPresence = DiscordRPC.RichPresence;
using Froststrap.Integrations.Discord;
using Froststrap.Vargostrap.Models;

namespace Froststrap.Vargostrap.Discord
{
    /// <summary>
    /// Builds the "Playing Vargostrap" presence from the user's customization (Integrations page), and
    /// optionally puts the Vargostrap logo into the in-game Roblox presence. Upstream's
    /// <c>FroststrapRichPresence</c> calls <see cref="Build"/> instead of hardcoding its presence.
    /// </summary>
    internal static class VargoRpc
    {
        /// <summary>Upstream's Discord application. Discord displays its name, "Froststrap".</summary>
        public const string UpstreamApplicationId = "1399535282713399418";

        /// <summary>Asset name to upload in your own Discord application's Rich Presence art assets.</summary>
        public const string LogoAssetKey = "vargostrap";

        public const int MaxTextLength = 128;
        public const int MaxButtonLabelLength = 32;
        public const int MaxUrlLength = 512;

        private static VargostrapSettings S => VargoApp.Settings.Prop;

        public static bool IsValidApplicationId(string? id) =>
            !string.IsNullOrWhiteSpace(id) && id.Trim().Length is >= 17 and <= 20 && id.Trim().All(char.IsAsciiDigit);

        public static bool HasCustomApplication => IsValidApplicationId(S.DiscordApplicationId);

        public static string ApplicationId => HasCustomApplication ? S.DiscordApplicationId.Trim() : UpstreamApplicationId;

        public static bool IsHttpsUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url)
            && url.Length <= MaxUrlLength
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps;

        /// <summary>Image key or URL for the logo, or null when no usable logo is configured.</summary>
        public static string? LogoImage
        {
            get
            {
                string logo = S.RpcLogo.Trim();

                if (IsHttpsUrl(logo))
                    return logo;

                if (logo.Length > 0 && !logo.Contains("://", StringComparison.Ordinal))
                    return logo; // asset key in the user's own Discord app

                return HasCustomApplication ? LogoAssetKey : null;
            }
        }

        public static string FormatText(string template, string page) =>
            template
                .Replace("{page}", page, StringComparison.OrdinalIgnoreCase)
                .Replace("{version}", App.Version, StringComparison.OrdinalIgnoreCase)
                .Trim();

        public static string DescribePage(string page, string? dialog) =>
            string.IsNullOrEmpty(dialog) ? page : $"{page} > {dialog}";

        /// <summary>Discord rejects text shorter than 2 characters, so those lines are dropped.</summary>
        private static string? Line(string template, string page)
        {
            string text = FormatText(template, page);
            return text.Length < 2 ? null : DiscordPresenceHelper.Truncate(text, MaxTextLength);
        }

        public static IEnumerable<(string Label, string Url)> ValidButtons()
        {
            foreach (var (label, url) in new[] { (S.RpcButton1Label, S.RpcButton1Url), (S.RpcButton2Label, S.RpcButton2Url) })
            {
                string l = label.Trim();
                if (l.Length is > 0 and <= MaxButtonLabelLength && IsHttpsUrl(url))
                    yield return (l, url.Trim());
            }
        }

        public static RichPresence Build(string page, string? dialog, Timestamps startTimestamps)
        {
            // The RPC library treats empty strings as "not set", the same convention upstream uses.
            string pageText = DescribePage(page, dialog);
            string logo = (S.RpcShowLogo ? LogoImage : null) ?? string.Empty;
            bool hasLogo = logo.Length > 0;
            // the version badge needs a large image to sit on, and only upstream's app has a "checkmark" asset
            bool versionBadge = hasLogo && S.RpcShowVersion && !HasCustomApplication;

            var presence = new RichPresence
            {
                Details = Line(S.RpcDetails, pageText) ?? string.Empty,
                State = Line(S.RpcState, pageText) ?? string.Empty,
                Timestamps = S.RpcShowTimer ? startTimestamps : new Timestamps(),
                Assets = new Assets
                {
                    LargeImageKey = logo,
                    LargeImageText = hasLogo ? App.ProjectName : string.Empty,
                    SmallImageKey = versionBadge ? "checkmark" : string.Empty,
                    SmallImageText = versionBadge ? DiscordPresenceHelper.Truncate(App.Version, MaxTextLength) : string.Empty,
                },
                Buttons = [.. ValidButtons().Select(b => new Button { Label = b.Label, Url = b.Url })],
            };

            // Without a small image there's nowhere for the version; show it on the large image instead.
            if (!versionBadge && S.RpcShowVersion && hasLogo)
                presence.Assets.LargeImageText = $"{App.ProjectName} {App.Version}";

            return presence;
        }

        /// <summary>Cheap identity for a presence, so unchanged presences aren't re-sent to Discord.</summary>
        public static string Fingerprint(RichPresence p) => string.Join('\u001f',
            p.Details, p.State, p.Timestamps?.Start is null, p.Assets?.LargeImageKey, p.Assets?.LargeImageText,
            p.Assets?.SmallImageKey, p.Assets?.SmallImageText,
            string.Join('|', p.Buttons?.Select(b => b.Label + "=" + b.Url) ?? []));

        /// <summary>
        /// Called by upstream's in-game presence while it builds its default assets. Replaces Roblox's
        /// small icon with the Vargostrap logo if enabled; the account avatar option still takes priority.
        /// </summary>
        public static void ApplyInGameLogo(ref string smallImage, ref string smallImageText)
        {
            try
            {
                if (S.RpcShowLogoInGame && IsHttpsUrl(S.RpcLogo))
                {
                    smallImage = S.RpcLogo.Trim();
                    smallImageText = $"Launched with {App.ProjectName}";
                }
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Vargostrap in-game logo skipped: {ex.Message}");
            }
        }

        /// <summary>Reconnects the app presence, e.g. after the application ID changed.</summary>
        public static void Reconnect()
        {
            if (!App.Settings.Prop.ShowUsingFroststrapRPC)
                return;

            App.FrostRPC = new Integrations.FroststrapRichPresence();
            App.FrostRPC.SetPage("Integrations");
        }
    }
}
