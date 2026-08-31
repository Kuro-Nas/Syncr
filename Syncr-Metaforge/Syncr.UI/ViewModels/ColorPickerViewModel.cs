using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Syncr.UI.ViewModels
{
    public class ColorPickerViewModel : ViewModelBase
    {
        private bool _updatingFromHex = false;


        private double _hue = 180;
        public double Hue
        {
            get => _hue;
            set
            {
                if (Math.Abs(_hue - value) < 0.001) return;
                _hue = Math.Clamp(value, 0, 360);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HueText));
                if (!_updatingFromHex) UpdateHexFromHsl();
            }
        }

        private double _saturation = 100;
        public double Saturation
        {
            get => _saturation;
            set
            {
                if (Math.Abs(_saturation - value) < 0.001) return;
                _saturation = Math.Clamp(value, 0, 100);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SatText));
                if (!_updatingFromHex) UpdateHexFromHsl();
            }
        }

        private double _lightness = 50;
        public double Lightness
        {
            get => _lightness;
            set
            {
                if (Math.Abs(_lightness - value) < 0.001) return;
                _lightness = Math.Clamp(value, 0, 100);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LightText));
                if (!_updatingFromHex) UpdateHexFromHsl();
            }
        }

        public string HueText => ((int)_hue).ToString();
        public string SatText => ((int)_saturation).ToString();
        public string LightText => ((int)_lightness).ToString();


        private Color _avaloniaColor = Colors.Cyan;
        public Color AvaloniaColor
        {
            get => _avaloniaColor;
            set
            {
                _avaloniaColor = value;
                OnPropertyChanged();
                string newHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                if (_hexValue != newHex)
                {
                    _hexValue = newHex;
                    OnPropertyChanged(nameof(HexValue));
                    OnPropertyChanged(nameof(PreviewColor));
                }
            }
        }

        private string _hexValue = "#00FFFF";
        public string HexValue
        {
            get => _hexValue;
            set
            {
                if (_hexValue == value) return;
                _hexValue = value ?? "#000000";
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewColor));

                if (Color.TryParse(_hexValue, out Color parsed))
                {
                    if (_avaloniaColor != parsed)
                    {
                        _avaloniaColor = parsed;
                        OnPropertyChanged(nameof(AvaloniaColor));
                    }
                }

                // Parse hex → update sliders without re-triggering hex update
                if (TryParseHex(_hexValue, out double h, out double s, out double l))
                {
                    _updatingFromHex = true;
                    Hue        = h;
                    Saturation = s;
                    Lightness  = l;
                    _updatingFromHex = false;
                }
            }
        }

        public string PreviewColor => _hexValue;


        public string SelectedColor { get; private set; }
        public bool Confirmed { get; private set; }


        public SimpleCommand<string> SetHexCommand  { get; }
        public SimpleCommand<Window> ApplyCommand   { get; }
        public SimpleCommand<Window> CancelCommand  { get; }

        public ColorPickerViewModel(string initialHex = "#00FFFF")
        {
            SetHexCommand = new SimpleCommand<string>(hex => HexValue = hex);
            ApplyCommand  = new SimpleCommand<Window>(ApplyAndClose);
            CancelCommand = new SimpleCommand<Window>(w => w?.Close());

            HexValue = initialHex ?? "#00FFFF";
            if (Color.TryParse(HexValue, out Color parsed))
            {
                _avaloniaColor = parsed;
            }
            SelectedColor = HexValue;
        }

        private void ApplyAndClose(Window w)
        {
            SelectedColor = _hexValue;
            Confirmed = true;
            w?.Close();
        }


        private void UpdateHexFromHsl()
        {
            _hexValue = HslToHex(_hue, _saturation / 100.0, _lightness / 100.0);
            OnPropertyChanged(nameof(HexValue));
            OnPropertyChanged(nameof(PreviewColor));
        }

        public static string HslToHex(double h, double s, double l)
        {
            // Standard HSL → RGB
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;

            double r = 0, g = 0, b = 0;
            if        (h < 60)  { r = c; g = x; b = 0; }
            else if   (h < 120) { r = x; g = c; b = 0; }
            else if   (h < 180) { r = 0; g = c; b = x; }
            else if   (h < 240) { r = 0; g = x; b = c; }
            else if   (h < 300) { r = x; g = 0; b = c; }
            else               { r = c; g = 0; b = x; }

            int R = (int)Math.Round((r + m) * 255);
            int G = (int)Math.Round((g + m) * 255);
            int B = (int)Math.Round((b + m) * 255);

            return $"#{R:X2}{G:X2}{B:X2}";
        }

        private static bool TryParseHex(string hex, out double h, out double s, out double l)
        {
            h = 0; s = 0; l = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                int R = Convert.ToInt32(hex[..2], 16);
                int G = Convert.ToInt32(hex[2..4], 16);
                int B = Convert.ToInt32(hex[4..6], 16);
                RgbToHsl(R / 255.0, G / 255.0, B / 255.0, out h, out s, out l);
                s *= 100; l *= 100;
                return true;
            }
            catch { return false; }
        }

        private static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            double delta = max - min;

            if (delta == 0) { h = s = 0; return; }

            s = l < 0.5 ? delta / (max + min) : delta / (2 - max - min);

            if      (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else               h = 60 * ((r - g) / delta + 4);

            if (h < 0) h += 360;
        }
    }
}
