/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.UI.Skills;
using Chummer.UI.Table;
using Microsoft.Win32;
using BorderStyle = System.Windows.Forms.BorderStyle;
using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;
using ListBox = System.Windows.Forms.ListBox;
using ListView = System.Windows.Forms.ListView;
using SystemColors = System.Drawing.SystemColors;
using TableCell = Chummer.UI.Table.TableCell;
using TextBox = System.Windows.Forms.TextBox;
using Timer = System.Timers.Timer;
using TreeNode = System.Windows.Forms.TreeNode;
using TreeView = System.Windows.Forms.TreeView;

namespace Chummer
{
    public static class ColorManager
    {
        // The setting for whether stuff uses dark mode or light mode is accessible purely through a specific registry key
        // So we save that key for accessing both at startup and should the setting be changed while Chummer is running
        private static readonly RegistryKey s_ObjPersonalizeKey;

        // While events that trigger on changes to a registry value are possible, they're a PITA in C#.
        // Checking for dark mode on a timer interval is less elegant, but also easier to set up, track, and debug.
        private static readonly Timer s_TmrDarkModeCheckerTimer;

        static ColorManager()
        {
            if (Utils.IsDesignerMode || Utils.IsRunningInVisualStudio)
                return;

            try
            {
                s_ObjPersonalizeKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            }
            catch (ObjectDisposedException)
            {
                //swallow this
            }
            catch (System.Security.SecurityException)
            {
                //swallow this
            }

            s_TmrDarkModeCheckerTimer = new Timer { Interval = 5000 }; // Poll registry every 5 seconds
            s_TmrDarkModeCheckerTimer.Elapsed += TmrDarkModeCheckerTimerOnTick;
            switch (GlobalSettings.ColorModeSetting)
            {
                case ColorMode.Automatic:
                    _blnIsLightMode = !DoesRegistrySayDarkMode();
                    break;

                case ColorMode.Light:
                    _blnIsLightMode = true;
                    break;

                case ColorMode.Dark:
                    _blnIsLightMode = false;
                    break;
            }
        }

        private static async void TmrDarkModeCheckerTimerOnTick(object sender, EventArgs e)
        {
            await AutoApplyLightDarkModeAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public static void AutoApplyLightDarkMode()
        {
            if (GlobalSettings.ColorModeSetting == ColorMode.Automatic)
            {
                IsLightMode = !DoesRegistrySayDarkMode();
                if (s_TmrDarkModeCheckerTimer != null)
                    s_TmrDarkModeCheckerTimer.Enabled = true;
            }
        }

        public static async Task AutoApplyLightDarkModeAsync(CancellationToken token = default)
        {
            if (GlobalSettings.ColorModeSetting == ColorMode.Automatic)
            {
                await SetIsLightModeAsync(!DoesRegistrySayDarkMode(), token).ConfigureAwait(false);
                if (s_TmrDarkModeCheckerTimer != null)
                    s_TmrDarkModeCheckerTimer.Enabled = true;
            }
        }

        public static bool DoesRegistrySayDarkMode()
        {
            object objLightModeResult = s_ObjPersonalizeKey?.GetValue("AppsUseLightTheme");
            if (objLightModeResult != null && int.TryParse(objLightModeResult.ToString(), out int intTemp))
                return intTemp == 0;
            return false;
        }

        public static void DisableAutoTimer()
        {
            if (s_TmrDarkModeCheckerTimer != null)
                s_TmrDarkModeCheckerTimer.Enabled = false;
        }

        private static bool _blnIsLightMode = true;

        public static bool IsLightMode
        {
            get => _blnIsLightMode;
            internal set
            {
                if (_blnIsLightMode == value)
                    return;
                _blnIsLightMode = value;
                ChummerMainForm frmMain = Program.MainForm;
                if (frmMain == null)
                    return;
                using (CursorWait.New(frmMain))
                    frmMain.UpdateLightDarkMode(CancellationToken.None);
            }
        }

        public static Task SetIsLightModeAsync(bool blnNewValue, CancellationToken token = default)
        {
            if (_blnIsLightMode == blnNewValue)
                return Task.CompletedTask;
            _blnIsLightMode = blnNewValue;
            ChummerMainForm frmMain = Program.MainForm;
            return frmMain == null ? Task.CompletedTask : Inner();
            async Task Inner()
            {
                CursorWait objCursorWait = await CursorWait.NewAsync(frmMain, token: token).ConfigureAwait(false);
                try
                {
                    await frmMain.UpdateLightDarkModeAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    await objCursorWait.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static readonly ConcurrentDictionary<Color, Color> s_DicDarkModeColors = new ConcurrentDictionary<Color, Color>();
        private static readonly ConcurrentDictionary<Color, Color> s_DicInverseDarkModeColors = new ConcurrentDictionary<Color, Color>();
        private static readonly ConcurrentDictionary<Color, Color> s_DicDimmedColors = new ConcurrentDictionary<Color, Color>();

        /// <summary>
        /// Returns a version of a color that has its lightness almost inverted (slightly increased lightness from inversion, slight desaturation)
        /// </summary>
        /// <param name="objColor">Color whose lightness and saturation should be adjusted for Dark Mode.</param>
        /// <returns>New Color object identical to <paramref name="objColor"/>, but with lightness and saturation adjusted for Dark Mode.</returns>
        public static Color GenerateDarkModeColor(Color objColor)
        {
            return s_DicDarkModeColors.GetOrAdd(objColor, GetDarkModeVersion);
        }

        /// <summary>
        /// Returns an inverted version of a color that has gone through GenerateDarkModeColor()
        /// </summary>
        /// <param name="objColor">Color whose Dark Mode conversions for lightness and saturation should be inverted.</param>
        /// <returns>New Color object identical to <paramref name="objColor"/>, but with its Dark Mode conversion inverted.</returns>
        public static Color GenerateInverseDarkModeColor(Color objColor)
        {
            return s_DicInverseDarkModeColors.GetOrAdd(objColor, InverseGetDarkModeVersion);
        }

        /// <summary>
        /// Returns a version of a color that has is adapted to the current Color mode setting (same color in Light mode, changed one in Dark mode)
        /// </summary>
        /// <param name="objColor">Color as it would be in Light mode</param>
        /// <returns>New Color object identical to <paramref name="objColor"/>, but potentially adapted to dark mode.</returns>
        public static Color GenerateCurrentModeColor(Color objColor)
        {
            return IsLightMode ? objColor : GenerateDarkModeColor(objColor);
        }

        /// <summary>
        /// Returns a version of a color that is independent of the current Color mode and can savely be used for storing.
        /// </summary>
        /// <param name="objColor">Color as it is shown in current color mode</param>
        /// <returns>New Color object identical to <paramref name="objColor"/>, but potentially adapted to light mode.</returns>
        public static Color GenerateModeIndependentColor(Color objColor)
        {
            return IsLightMode ? objColor : GenerateInverseDarkModeColor(objColor);
        }

        /// <summary>
        /// Returns a version of a (Mode-Independent) color that has its lightness dimmed down in Light mode or brightened in Dark Mode
        /// </summary>
        /// <param name="objColor">Color (in Mode-Independent form) whose lightness should be dimmed.</param>
        /// <returns>New Color object identical to <paramref name="objColor"/>, but with its lightness values dimmed or brightened as appropriate.</returns>
        public static Color GenerateCurrentModeDimmedColor(Color objColor)
        {
            return IsLightMode
                ? s_DicDimmedColors.GetOrAdd(objColor, GetDimmedVersion)
                : GetDarkModeVersion(s_DicDimmedColors.GetOrAdd(objColor, GetDimmedVersion));
        }

        /// <summary>
        /// Because the transforms applied to convert a Light Mode color to Dark Mode cannot produce some ranges of lightness and saturation, not all colors are valid in Dark Mode.
        /// This function takes a color intended for Dark Mode and converts it to the closest possible color that is valid in Dark Mode.
        /// If the original color is valid in Dark Mode to begin with, the transforms should end up reproducing it.
        /// </summary>
        /// <param name="objColor">Color to adjust, originally specified within Dark Mode.</param>
        /// <returns>New Color very similar to <paramref name="objColor"/>, but with lightness and saturation values set to within the range allowable in Dark Mode.</returns>
        private static Color TransformToDarkModeValidVersion(Color objColor)
        {
            return GenerateDarkModeColor(GenerateInverseDarkModeColor(objColor));
        }

        public static Color WindowText => IsLightMode ? WindowTextLight : WindowTextDark;
        internal static Color WindowTextLight { get; } = SystemColors.WindowText;
        internal static Color WindowTextDark { get; } = GenerateDarkModeColor(WindowTextLight);

        public static Color Window => IsLightMode ? WindowLight : WindowDark;
        internal static Color WindowLight { get; } = SystemColors.Window;
        internal static Color WindowDark { get; } = GenerateDarkModeColor(WindowLight);

        public static Color InfoText => IsLightMode ? InfoTextLight : InfoTextDark;
        private static Color InfoTextLight { get; } = SystemColors.InfoText;
        private static Color InfoTextDark { get; } = GenerateDarkModeColor(InfoTextLight);

        public static Color Info => IsLightMode ? InfoLight : InfoDark;
        private static Color InfoLight { get; } = SystemColors.Info;
        private static Color InfoDark { get; } = GenerateDarkModeColor(InfoLight);

        public static Color GrayText => IsLightMode ? GrayTextLight : GrayTextDark;
        private static Color GrayTextLight { get; } = SystemColors.GrayText;
        internal static Color GrayTextDark { get; } = GenerateDarkModeColor(GrayTextLight);

        public static Color HighlightText => IsLightMode ? HighlightTextLight : HighlightTextDark;
        private static Color HighlightTextLight { get; } = SystemColors.HighlightText;
        private static Color HighlightTextDark { get; } = GenerateDarkModeColor(HighlightTextLight);

        public static Color Highlight => IsLightMode ? HighlightLight : HighlightDark;
        private static Color HighlightLight { get; } = SystemColors.Highlight;
        private static Color HighlightDark { get; } = GenerateDarkModeColor(HighlightLight);

        public static Color ControlText => IsLightMode ? ControlTextLight : ControlTextDark;
        public static Color ControlTextLight { get; } = SystemColors.ControlText;
        internal static Color ControlTextDark { get; } = GenerateDarkModeColor(ControlTextLight);

        public static Color ControlDarkest => IsLightMode ? ControlDarkestLight : ControlDarkestDark;
        public static Color ControlDarkestLight { get; } = SystemColors.ControlDarkDark;
        internal static Color ControlDarkestDark { get; } = GenerateDarkModeColor(ControlDarkestLight);

        public static Color ControlDarker => IsLightMode ? ControlDarkerLight : ControlDarkerDark;
        public static Color ControlDarkerLight { get; } = SystemColors.ControlDark;
        internal static Color ControlDarkerDark { get; } = GenerateDarkModeColor(ControlDarkerLight);

        public static Color Control => IsLightMode ? ControlLight : ControlDark;
        public static Color ControlLight { get; } = SystemColors.Control;
        internal static Color ControlDark { get; } = GenerateDarkModeColor(ControlLight);

        public static Color ControlLighter => IsLightMode ? ControlLighterLight : ControlLighterDark;
        internal static Color ControlLighterLight { get; } = SystemColors.ControlLight;
        internal static Color ControlLighterDark { get; } = GenerateDarkModeColor(ControlLight);

        public static Color ControlLightest => IsLightMode ? ControlLightestLight : ControlLightestDark;
        internal static Color ControlLightestLight { get; } = SystemColors.ControlLightLight;
        internal static Color ControlLightestDark { get; } = GenerateDarkModeColor(ControlLightestLight);

        public static Color ButtonFace => IsLightMode ? ButtonFaceLight : ButtonFaceDark;
        private static Color ButtonFaceLight { get; } = SystemColors.ButtonFace;
        private static Color ButtonFaceDark { get; } = GenerateDarkModeColor(ButtonFaceLight);

        public static Color ButtonShadow => IsLightMode ? ButtonShadowLight : ButtonShadowDark;
        private static Color ButtonShadowLight { get; } = SystemColors.ButtonShadow;
        private static Color ButtonShadowDark { get; } = GenerateDarkModeColor(ButtonShadowLight);

        public static Color AppWorkspace => IsLightMode ? AppWorkspaceLight : AppWorkspaceDark;
        internal static Color AppWorkspaceLight { get; } = SystemColors.AppWorkspace;
        internal static Color AppWorkspaceDark { get; } = GenerateDarkModeColor(AppWorkspaceLight);

        public static Color SplitterColor => IsLightMode ? SplitterColorLight : SplitterColorDark;
        internal static Color SplitterColorLight { get; } = SystemColors.InactiveCaption;
        internal static Color SplitterColorDark { get; } = GenerateDarkModeColor(SplitterColorLight);

        public static Color HasNotesColor => IsLightMode ? HasNotesColorLight : HasNotesColorDark;
        public static Color HasNotesColorLight => GlobalSettings.DefaultHasNotesColor;
        public static Color HasNotesColorDark => GenerateDarkModeColor(HasNotesColorLight);

        public static Color GrayHasNotesColor => IsLightMode ? GrayHasNotesColorLight : GrayHasNotesColorDark;
        internal static Color GrayHasNotesColorLight => s_DicDimmedColors.GetOrAdd(HasNotesColorLight, GetDimmedVersion);
        internal static Color GrayHasNotesColorDark => GenerateDarkModeColor(GrayHasNotesColorLight);

        public static Color ErrorColor { get; } = Color.Red;

        public static Color DieGlitchFore => IsLightMode ? DieGlitchForeLight : DieGlitchForeDark;
        internal static Color DieGlitchForeLight => WindowTextDark;
        internal static Color DieGlitchForeDark => GenerateDarkModeColor(DieGlitchForeLight);
        public static Color DieGlitchBackground => IsLightMode ? DieGlitchBackgroundLight : DieGlitchBackgroundDark;
        internal static Color DieGlitchBackgroundLight => ControlDarkerLight;
        internal static Color DieGlitchBackgroundDark => GenerateDarkModeColor(DieGlitchBackgroundLight);
        public static Color DieHitFore => IsLightMode ? DieHitForeLight : DieHitForeDark;
        internal static Color DieHitForeLight => ControlTextLight;
        internal static Color DieHitForeDark => GenerateDarkModeColor(DieHitForeLight);
        public static Color DieHitBackground => IsLightMode ? DieHitBackgroundLight : DieHitBackgroundDark;
        internal static Color DieHitBackgroundLight { get; } = Color.LightGreen;
        internal static Color DieHitBackgroundDark => GenerateDarkModeColor(DieHitBackgroundLight);
        public static Color DieGlitchHitFore => IsLightMode ? DieGlitchHitForeLight : DieGlitchHitForeDark;
        internal static Color DieGlitchHitForeLight => ControlTextLight;
        internal static Color DieGlitchHitForeDark => GenerateDarkModeColor(DieHitForeLight);
        public static Color DieGlitchHitBackground => IsLightMode ? DieGlitchHitBackgroundLight : DieGlitchHitBackgroundDark;
        internal static Color DieGlitchHitBackgroundLight { get; } = Color.DarkGreen;
        internal static Color DieGlitchHitBackgroundDark => GenerateDarkModeColor(DieHitBackgroundLight);

        #region Color Inversion Methods

        /// <summary>
        /// Converts a color to the version it would have in Dark Mode.
        /// Lightness is inverted and then increased nonlinearly.
        /// Saturation is slightly decreased nonlinearly.
        /// </summary>
        /// <param name="objColor"></param>
        /// <returns></returns>
        private static Color GetDarkModeVersion(Color objColor)
        {
            float fltHue = objColor.GetHue() / 360.0f;
            float fltLightness = objColor.GetBrightness(); // It's called Brightness, but it's actually Lightness
            float fltNewLightness = 1.0f - fltLightness;
            float fltNewValue = fltNewLightness + objColor.GetSaturation() * Math.Min(fltNewLightness, 1 - fltNewLightness);
            float fltSaturationHsv = Math.Abs(fltNewValue) < float.Epsilon ? 0 : 2 * (1 - fltNewLightness / fltNewValue);
            // Lighten dark colors a little by increasing value so that we don't warp colors that are highly saturated to begin with.
            fltNewValue += 0.14f * (1.0f - Convert.ToSingle(Math.Sqrt(fltNewValue)));
            fltNewValue = Math.Min(fltNewValue, 1.0f);
            Color objColorIntermediate = FromHsv(fltHue, fltSaturationHsv, fltNewValue);
            fltNewLightness = objColorIntermediate.GetBrightness();
            fltNewValue = fltNewLightness + objColorIntermediate.GetSaturation() * Math.Min(fltNewLightness, 1 - fltNewLightness);
            fltSaturationHsv = Math.Abs(fltNewValue) < float.Epsilon ? 0 : 2 * (1 - fltNewLightness / fltNewValue);
            // Desaturate high saturation colors a little
            float fltNewSaturationHsv = fltSaturationHsv - 0.1f * fltSaturationHsv * fltSaturationHsv;
            return FromHsva(fltHue, fltNewSaturationHsv, fltNewValue, objColor.A);
        }

        /// <summary>
        /// Inverse operation of <see cref="GetDarkModeVersion(Color)"/>. If a color is fed through that function, and the result is then fed through this one, the final result should be the original color.
        /// Note that because <see cref="GetDarkModeVersion(Color)"/> always does some amount of desaturation and lightening, not all colors are valid results of <see cref="GetDarkModeVersion(Color)"/>.
        /// This function should therefore *not* be used as a kind of "GetLightModeVersion" of a dark mode color directly.
        /// </summary>
        private static Color InverseGetDarkModeVersion(Color objColor)
        {
            float fltHue = objColor.GetHue() / 360.0f;
            float fltLightness = objColor.GetBrightness(); // It's called Brightness, but it's actually Lightness
            float fltValue = fltLightness + objColor.GetSaturation() * Math.Min(fltLightness, 1 - fltLightness);
            float fltSaturationHsv = Math.Abs(fltValue) < float.Epsilon ? 0 : 2 * (1 - fltLightness / fltValue);
            float fltNewSaturationHsv = 0;
            if (Math.Abs(fltSaturationHsv) >= float.Epsilon)
            {
                // x - 0.1x^2 = n is the regular transform where x is the Light Mode saturation and n is the Dark Mode saturation
                // To get it back, we need to solve for x knowing only n:
                // x^2 - 10x + 10n = 0
                // x = (10 +/- sqrt(100 - 40n))/2 = 5 +/- sqrt(25 - 10n)
                // Because saturation cannot be greater than 1, positive result is unreal, therefore: x = 5 - sqrt(25 - 10n)
                fltNewSaturationHsv = Math.Min((float)(5.0 - Math.Sqrt(25.0 - 10.0 * fltSaturationHsv)), 1.0f);
                Color objColorIntermediate = FromHsv(fltHue, fltNewSaturationHsv, fltValue);
                fltLightness = objColorIntermediate.GetBrightness(); // It's called Brightness, but it's actually Lightness
                fltValue = fltLightness + objColorIntermediate.GetSaturation() * Math.Min(fltLightness, 1 - fltLightness);
            }
            // y + 0.14(1 - sqrt(y)) = m is the regular transform where y is the Dark Mode Value pre-adjustment and m is the Dark Mode Value post-adjustment
            // To get it back, we need to solve for y knowing only m:
            // 1 - sqrt(y) + 50/7*y - 50/7*m = 0
            // 1 + 50/7*y - 50/7*m = sqrt(y)
            // 1 + 2500/49*y^2 + 2500/49*m^2 + 100/7*y - 100/7*m - 5000/49*my = y
            // 2500/49*y^2 + (93/7 - 5000/49*m)y + 2500/49*m^2 - 100/7*m + 1 = 0
            // 2500/7*y^2 + (93 - 5000m/7)y + 2500/7*m^2 - 100m + 7 = 0
            // y = (5000m/7 - 93 +/- sqrt((93 - 5000m/7)^2 - 10000/7*(2500/7*m^2 - 100m + 7)))/5000*7
            // y = (5000m - 651 +/- 7*sqrt(25000000m^2/49 - 930000m/7 + 8649 - 25000000m^2/49 + 1000000m/7 - 10000))/5000
            // y = (5000m - 651 +/- 7*sqrt(10000m - 1351))/5000
            // y = m - 0.1302 +/- 0.14*sqrt(m - 0.1351)
            // Because expression for y must be the same across all m, only positive result is valid, therefore: y = m - 0.1302 + 0.14*sqrt(m - 0.1351)
            float fltNewValue = Math.Min((float)(fltValue - 0.1302 + 0.14 * Math.Sqrt(fltValue - 0.1351)), 1.0f);
            // Now convert to Lightness so we can flip it
            float fltNewLightness = fltNewValue * (1 - fltNewSaturationHsv / 2.0f);
            float fltDivisor = Math.Min(fltNewLightness, 1 - fltNewLightness);
            float fltNewSaturationHsl = Math.Abs(fltDivisor) < float.Epsilon
                ? 1
                : (fltNewValue - fltNewLightness) / fltDivisor;
            fltNewLightness = 1 - fltNewLightness;
            return FromHsla(fltHue, fltNewSaturationHsl, fltNewLightness, objColor.A);
        }

        private static Color GetDimmedVersion(Color objColor)
        {
            // Built-in functions are in HSV/HSB, so we need to convert to HSL to invert lightness.
            float fltHue = objColor.GetHue() / 360.0f;
            float fltBrightness = objColor.GetBrightness();
            float fltSaturation = objColor.GetSaturation();
            fltSaturation = Math.Max(0, fltSaturation * 0.625f);
            return FromHsva(fltHue, fltBrightness, fltSaturation, objColor.A);
        }

        #endregion Color Inversion Methods

        #region Color Utility Methods

        /// <summary>
        /// Returns an RGB Color from HSL values.
        /// </summary>
        /// <param name="fltHue">Hue value, between 0.0 and 1.0.</param>
        /// <param name="fltSaturation">Saturation value, between 0.0 and 1.0.</param>
        /// <param name="fltLightness">Lightness value, between 0.0 and 1.0.</param>
        /// <returns>A Color with RGB values corresponding to the HSL inputs.</returns>
        public static Color FromHsl(float fltHue, float fltSaturation, float fltLightness)
        {
            return FromHsla(fltHue, fltSaturation, fltLightness, byte.MaxValue);
        }

        /// <summary>
        /// Returns an RGBA Color from HSLA values.
        /// </summary>
        /// <param name="fltHue">Hue value, between 0.0 and 1.0.</param>
        /// <param name="fltSaturation">Saturation value, between 0.0 and 1.0.</param>
        /// <param name="fltLightness">Lightness value, between 0.0 and 1.0.</param>
        /// <param name="chrAlpha">Alpha value.</param>
        /// <returns>A Color with RGBA values corresponding to the HSLA inputs.</returns>
        public static Color FromHsla(float fltHue, float fltSaturation, float fltLightness, byte chrAlpha)
        {
            if (fltHue > 1.0f || fltHue < 0)
                throw new ArgumentOutOfRangeException(nameof(fltHue));
            if (fltSaturation > 1.0f || fltSaturation < 0)
                throw new ArgumentOutOfRangeException(nameof(fltSaturation));
            if (fltLightness > 1.0f || fltLightness < 0)
                throw new ArgumentOutOfRangeException(nameof(fltLightness));
            // Ramp up precision for intermediates
            double dblChroma = fltSaturation * (1.0 - Math.Abs(2.0 * fltLightness - 1.0));
            double dblCommon = fltLightness - dblChroma / 2.0;
            double dblCardinalHue = fltHue * 6.0;
            double dblCardinalHueMod2 = dblCardinalHue;
            while (dblCardinalHueMod2 > 2.0)
                dblCardinalHueMod2 -= 2.0;
            double dblMinorChroma = dblChroma * (1.0 - Math.Abs(dblCardinalHueMod2 - 1.0));

            double dblRed = dblCommon;
            double dblGreen = dblCommon;
            double dblBlue = dblCommon;
            switch ((int)Math.Floor(dblCardinalHue))
            {
                case 6:
                case 5:
                    dblRed += dblChroma;
                    dblBlue += dblMinorChroma;
                    break;

                case 4:
                    dblRed += dblMinorChroma;
                    dblBlue += dblChroma;
                    break;

                case 3:
                    dblGreen += dblMinorChroma;
                    dblBlue += dblChroma;
                    break;

                case 2:
                    dblGreen += dblChroma;
                    dblBlue += dblMinorChroma;
                    break;

                case 1:
                    dblRed += dblMinorChroma;
                    dblGreen += dblChroma;
                    break;

                default:
                    dblRed += dblChroma;
                    dblGreen += dblMinorChroma;
                    break;
            }

            dblRed = Math.Round(dblRed * byte.MaxValue, MidpointRounding.AwayFromZero);
            dblGreen = Math.Round(dblGreen * byte.MaxValue, MidpointRounding.AwayFromZero);
            dblBlue = Math.Round(dblBlue * byte.MaxValue, MidpointRounding.AwayFromZero);

            return Color.FromArgb(chrAlpha,
                Math.Max(Math.Min(double.IsNaN(dblRed) ? 0 : Convert.ToInt32(dblRed), byte.MaxValue), byte.MinValue),
                Math.Max(Math.Min(double.IsNaN(dblGreen) ? 0 : Convert.ToInt32(dblGreen), byte.MaxValue), byte.MinValue),
                Math.Max(Math.Min(double.IsNaN(dblBlue) ? 0 : Convert.ToInt32(dblBlue), byte.MaxValue), byte.MinValue));
        }

        /// <summary>
        /// Returns an RGB Color from HSV (or HSB) values.
        /// </summary>
        /// <param name="fltHue">Hue value, between 0.0 and 1.0.</param>
        /// <param name="fltSaturation">Saturation value, between 0.0 and 1.0.</param>
        /// <param name="fltValue">Value/Brightness value, between 0.0 and 1.0.</param>
        /// <returns>A Color with RGB values corresponding to the HSV inputs.</returns>
        public static Color FromHsv(float fltHue, float fltSaturation, float fltValue)
        {
            return FromHsva(fltHue, fltSaturation, fltValue, byte.MaxValue);
        }

        /// <summary>
        /// Returns an RGBA Color from HSVA (or HSBA) values.
        /// </summary>
        /// <param name="fltHue">Hue value, between 0.0 and 1.0.</param>
        /// <param name="fltSaturation">Saturation value, between 0.0 and 1.0.</param>
        /// <param name="fltValue">Value/Brightness value, between 0.0 and 1.0.</param>
        /// <param name="chrAlpha">Alpha value.</param>
        /// <returns>A Color with RGBA values corresponding to the HSVA inputs.</returns>
        public static Color FromHsva(float fltHue, float fltSaturation, float fltValue, byte chrAlpha)
        {
            if (fltHue > 1.0f || fltHue < 0)
                throw new ArgumentOutOfRangeException(nameof(fltHue));
            if (fltSaturation > 1.0f || fltSaturation < 0)
                throw new ArgumentOutOfRangeException(nameof(fltSaturation));
            if (fltValue > 1.0f || fltValue < 0)
                throw new ArgumentOutOfRangeException(nameof(fltValue));
            // Ramp up precision for intermediates
            double dblChroma = fltSaturation * (double)fltValue;
            double dblCommon = fltValue - dblChroma;
            double dblCardinalHue = fltHue * 6.0;
            double dblCardinalHueMod2 = dblCardinalHue;
            while (dblCardinalHueMod2 > 2.0)
                dblCardinalHueMod2 -= 2.0;
            double dblMinorChroma = dblChroma * (1.0 - Math.Abs(dblCardinalHueMod2 - 1.0));

            double dblRed = dblCommon;
            double dblGreen = dblCommon;
            double dblBlue = dblCommon;
            switch ((int)Math.Floor(dblCardinalHue))
            {
                case 6:
                case 5:
                    dblRed += dblChroma;
                    dblBlue += dblMinorChroma;
                    break;

                case 4:
                    dblRed += dblMinorChroma;
                    dblBlue += dblChroma;
                    break;

                case 3:
                    dblGreen += dblMinorChroma;
                    dblBlue += dblChroma;
                    break;

                case 2:
                    dblGreen += dblChroma;
                    dblBlue += dblMinorChroma;
                    break;

                case 1:
                    dblRed += dblMinorChroma;
                    dblGreen += dblChroma;
                    break;

                default:
                    dblRed += dblChroma;
                    dblGreen += dblMinorChroma;
                    break;
            }

            dblRed = Math.Round(dblRed * byte.MaxValue, MidpointRounding.AwayFromZero);
            dblGreen = Math.Round(dblGreen * byte.MaxValue, MidpointRounding.AwayFromZero);
            dblBlue = Math.Round(dblBlue * byte.MaxValue, MidpointRounding.AwayFromZero);

            return Color.FromArgb(chrAlpha,
                Math.Max(Math.Min(double.IsNaN(dblRed) ? 0 : Convert.ToInt32(dblRed), byte.MaxValue), byte.MinValue),
                Math.Max(Math.Min(double.IsNaN(dblGreen) ? 0 : Convert.ToInt32(dblGreen), byte.MaxValue), byte.MinValue),
                Math.Max(Math.Min(double.IsNaN(dblBlue) ? 0 : Convert.ToInt32(dblBlue), byte.MaxValue), byte.MinValue));
        }

        #endregion Color Utility Methods
    }
}
