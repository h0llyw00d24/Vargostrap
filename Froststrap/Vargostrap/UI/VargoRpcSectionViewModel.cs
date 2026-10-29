// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.ViewModels;
using Froststrap.Vargostrap.Discord;
using Froststrap.Vargostrap.Models;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>
    /// "Customize Vargostrap RPC" section on the Integrations page. Edits apply to Discord right away
    /// (so the preview and your profile match) and are written to disk with the window's Save button.
    /// </summary>
    internal sealed class VargoRpcSectionViewModel : NotifyPropertyChangedViewModel
    {
        private static VargostrapSettings S => VargoApp.Settings.Prop;

        private string _applicationIdInput = S.DiscordApplicationId;

        private void Changed(params string[] names)
        {
            foreach (string name in names)
                OnPropertyChanged(name);

            foreach (string preview in (string[])[nameof(PreviewDetails), nameof(PreviewState), nameof(PreviewShowsTimer),
                nameof(PreviewShowsLogo), nameof(PreviewLogoHint), nameof(PreviewButton1), nameof(PreviewButton2),
                nameof(HasPreviewButton1), nameof(HasPreviewButton2), nameof(LogoStatus), nameof(InGameLogoAvailable)])
            {
                OnPropertyChanged(preview);
            }

            App.FrostRPC?.Refresh();
        }

        #region Text

        public string Details
        {
            get => S.RpcDetails;
            set { S.RpcDetails = Clip(value, VargoRpc.MaxTextLength); Changed(nameof(Details)); }
        }

        public string State
        {
            get => S.RpcState;
            set { S.RpcState = Clip(value, VargoRpc.MaxTextLength); Changed(nameof(State)); }
        }

        public bool ShowTimer
        {
            get => S.RpcShowTimer;
            set { S.RpcShowTimer = value; Changed(nameof(ShowTimer)); }
        }

        public bool ShowVersion
        {
            get => S.RpcShowVersion;
            set { S.RpcShowVersion = value; Changed(nameof(ShowVersion)); }
        }

        #endregion

        #region Logo

        public bool ShowLogo
        {
            get => S.RpcShowLogo;
            set { S.RpcShowLogo = value; Changed(nameof(ShowLogo)); }
        }

        public string Logo
        {
            get => S.RpcLogo;
            set { S.RpcLogo = value?.Trim() ?? ""; Changed(nameof(Logo)); }
        }

        public static string LogoStatus
        {
            get
            {
                string? logo = VargoRpc.LogoImage;

                if (logo is null)
                    return "No logo configured yet. Discord will show the application's own icon. Paste an https image link above, or set your own application ID and upload an art asset named \"vargostrap\".";

                return VargoRpc.IsHttpsUrl(logo)
                    ? "Using the image link above."
                    : $"Using the art asset \"{logo}\" from your Discord application.";
            }
        }

        public bool ShowLogoInGame
        {
            get => S.RpcShowLogoInGame;
            set { S.RpcShowLogoInGame = value; Changed(nameof(ShowLogoInGame)); }
        }

        /// <summary>Roblox's presence runs under Roblox's Discord app, so only an image URL works there.</summary>
        public static bool InGameLogoAvailable => VargoRpc.IsHttpsUrl(S.RpcLogo);

        #endregion

        #region Buttons

        public string Button1Label
        {
            get => S.RpcButton1Label;
            set { S.RpcButton1Label = Clip(value, VargoRpc.MaxButtonLabelLength); Changed(nameof(Button1Label)); }
        }

        public string Button1Url
        {
            get => S.RpcButton1Url;
            set { S.RpcButton1Url = value?.Trim() ?? ""; Changed(nameof(Button1Url)); }
        }

        public string Button2Label
        {
            get => S.RpcButton2Label;
            set { S.RpcButton2Label = Clip(value, VargoRpc.MaxButtonLabelLength); Changed(nameof(Button2Label)); }
        }

        public string Button2Url
        {
            get => S.RpcButton2Url;
            set { S.RpcButton2Url = value?.Trim() ?? ""; Changed(nameof(Button2Url)); }
        }

        #endregion

        #region Discord application

        public string ApplicationIdInput
        {
            get => _applicationIdInput;
            set
            {
                _applicationIdInput = value?.Trim() ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(ApplicationIdError));
                OnPropertyChanged(nameof(CanApplyApplicationId));
            }
        }

        public string? ApplicationIdError => _applicationIdInput.Length == 0 || VargoRpc.IsValidApplicationId(_applicationIdInput)
            ? null
            : "An application ID is a 17–20 digit number from the Discord Developer Portal.";

        public bool CanApplyApplicationId => ApplicationIdError is null && _applicationIdInput != S.DiscordApplicationId;

        public static string ApplicationStatus => VargoRpc.HasCustomApplication
            ? $"Using your Discord application ({S.DiscordApplicationId})."
            : "Using Froststrap's Discord application, so Discord shows \"Playing Froststrap\". Create your own (free) to show \"Playing Vargostrap\".";

        public ICommand ApplyApplicationIdCommand => new RelayCommand(() =>
        {
            S.DiscordApplicationId = _applicationIdInput;
            VargoRpc.Reconnect();
            Changed(nameof(CanApplyApplicationId), nameof(ApplicationStatus), nameof(PreviewAppName));
        });

        public static ICommand OpenDeveloperPortalCommand => GlobalViewModel.OpenWebpageCommand;

        public static string DeveloperPortalUrl => "https://discord.com/developers/applications";

        #endregion

        #region Preview

        private const string PreviewPage = "Integrations";

        public static string PreviewAppName => VargoRpc.HasCustomApplication ? "Your Discord app" : "Froststrap";

        public static string? PreviewDetails => Preview(S.RpcDetails);

        public static string? PreviewState => Preview(S.RpcState);

        public static bool PreviewShowsTimer => S.RpcShowTimer;

        public static bool PreviewShowsLogo => S.RpcShowLogo && VargoRpc.LogoImage is not null;

        public static string PreviewLogoHint => !S.RpcShowLogo
            ? "Logo hidden"
            : VargoRpc.LogoImage is null ? "App icon (no logo set)" : "Vargostrap logo";

        public static string? PreviewButton1 => VargoRpc.ValidButtons().Select(b => b.Label).FirstOrDefault();

        public static string? PreviewButton2 => VargoRpc.ValidButtons().Select(b => b.Label).Skip(1).FirstOrDefault();

        public static bool HasPreviewButton1 => PreviewButton1 is not null;

        public static bool HasPreviewButton2 => PreviewButton2 is not null;

        private static string? Preview(string template)
        {
            string text = VargoRpc.FormatText(template, PreviewPage);
            return text.Length < 2 ? null : text;
        }

        #endregion

        public ICommand ResetCommand => new RelayCommand(() =>
        {
            var defaults = new VargostrapSettings();
            S.RpcDetails = defaults.RpcDetails;
            S.RpcState = defaults.RpcState;
            S.RpcShowTimer = defaults.RpcShowTimer;
            S.RpcShowVersion = defaults.RpcShowVersion;
            S.RpcShowLogo = defaults.RpcShowLogo;
            S.RpcButton1Label = defaults.RpcButton1Label;
            S.RpcButton1Url = defaults.RpcButton1Url;
            S.RpcButton2Label = defaults.RpcButton2Label;
            S.RpcButton2Url = defaults.RpcButton2Url;
            S.RpcShowLogoInGame = defaults.RpcShowLogoInGame;
            // logo and application ID are setup, not styling; Reset keeps them

            Changed(string.Empty);
        });

        private static string Clip(string? value, int max)
        {
            value ??= "";
            return value.Length > max ? value[..max] : value;
        }
    }
}
