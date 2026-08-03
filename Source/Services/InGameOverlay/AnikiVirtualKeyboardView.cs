using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AnikiHelper.Services.InGameOverlay
{
    internal sealed class AnikiVirtualKeyboardView : Grid
    {
        private enum VirtualKeyKind
        {
            Character,
            ToggleSymbols,
            ToggleCase,
            ToggleSymbolPage,
            Space,
            Backspace,
            CursorLeft,
            CursorRight,
            Submit,
            SubmitAndEnter,
            Cancel
        }

        private sealed class VirtualKeySpec
        {
            public VirtualKeyKind Kind { get; set; }
            public string Value { get; set; }
            public string Label { get; set; }
            public double WidthWeight { get; set; } = 1;
        }

        private static readonly uint[] PhysicalTopRowScanCodes =
        {
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19
        };

        private static readonly uint[] PhysicalMiddleRowScanCodes =
        {
            0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27
        };

        private static readonly uint[] PhysicalBottomRowScanCodes =
        {
            0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32
        };

        private static readonly string[] UsefulBottomRowFillers =
        {
            "'", "-", "_", "."
        };

        private static readonly HashSet<string> AlphaModePunctuation = new HashSet<string>(StringComparer.Ordinal)
        {
            ";", ":", ",", ".", "'", "\"", "-", "_", "?", "!"
        };

        private readonly Func<string, string, string> loc;
        private readonly Func<IntPtr> getKeyboardLayout;
        private readonly Action<string, bool> submitAction;
        private readonly Action closedAction;
        private readonly List<List<Button>> buttonRows = new List<List<Button>>();
        private readonly Dictionary<Button, VirtualKeySpec> buttonSpecs = new Dictionary<Button, VirtualKeySpec>();

        private Grid keysGrid;
        private TextBlock inputText;
        private TextBlock modeText;
        private int selectedRow;
        private int selectedColumn;
        private int cursorPosition;
        private string currentText = string.Empty;
        private bool isUppercase;
        private bool isSymbols;
        private bool isExtendedSymbols;
        private IntPtr activeKeyboardLayout;
        private DateTime lastControllerInputTime = DateTime.MinValue;
        private ControllerInput? lastControllerInputButton;
        private DateTime lastDirectionalInputTime = DateTime.MinValue;
        private int lastDirectionalHorizontal;
        private int lastDirectionalVertical;

        // Playnite/WPF can expose one D-pad press both as a controller event and
        // as an arrow-key event. Ignore only the near-simultaneous duplicate.
        private const int DirectionalDuplicateWindowMs = 120;

        private const uint MAPVK_VSC_TO_VK_EX = 3;
        private const uint TO_UNICODE_NO_STATE_CHANGE = 0x4;
        private const int VK_SHIFT = 0x10;

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        public AnikiVirtualKeyboardView(
            Func<string, string, string> loc,
            Func<IntPtr> getKeyboardLayout,
            Action<string, bool> submitAction,
            Action closedAction)
        {
            this.loc = loc;
            this.getKeyboardLayout = getKeyboardLayout;
            this.submitAction = submitAction;
            this.closedAction = closedAction;

            Visibility = Visibility.Collapsed;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            Focusable = false;
            IsHitTestVisible = true;

            BuildUi();
        }

        public void Open()
        {
            currentText = string.Empty;
            cursorPosition = 0;
            isUppercase = false;
            isSymbols = false;
            isExtendedSymbols = false;
            activeKeyboardLayout = ResolveKeyboardLayout();
            selectedRow = 0;
            selectedColumn = 0;
            lastControllerInputTime = DateTime.MinValue;
            lastControllerInputButton = null;
            lastDirectionalInputTime = DateTime.MinValue;
            lastDirectionalHorizontal = 0;
            lastDirectionalVertical = 0;

            RebuildKeys();
            RefreshInputText();
            Visibility = Visibility.Visible;
            UpdateSelectionVisuals();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            Visibility = Visibility.Collapsed;
            closedAction?.Invoke();
        }

        public bool HandleControllerInput(ControllerInput button)
        {
            if (!IsOpen)
            {
                return false;
            }

            var now = DateTime.Now;
            if (lastControllerInputButton.HasValue &&
                lastControllerInputButton.Value == button &&
                (now - lastControllerInputTime).TotalMilliseconds < 160)
            {
                return true;
            }

            lastControllerInputButton = button;
            lastControllerInputTime = now;

            switch (button)
            {
                case ControllerInput.DPadLeft:
                case ControllerInput.LeftStickLeft:
                    TryMoveHorizontal(-1);
                    break;

                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickRight:
                    TryMoveHorizontal(1);
                    break;

                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp:
                    TryMoveVertical(-1);
                    break;

                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown:
                    TryMoveVertical(1);
                    break;

                case ControllerInput.A:
                    ExecuteSelectedKey();
                    break;

                case ControllerInput.X:
                    Backspace();
                    break;

                case ControllerInput.Y:
                    AppendText(" ");
                    break;

                case ControllerInput.Start:
                    Submit(false);
                    break;

                case ControllerInput.B:
                case ControllerInput.Back:
                    Close();
                    break;
            }

            return true;
        }

        public bool HandlePreviewKeyDown(KeyEventArgs e)
        {
            if (!IsOpen || e == null)
            {
                return false;
            }

            switch (e.Key)
            {
                case Key.Left:
                    TryMoveHorizontal(-1);
                    break;
                case Key.Right:
                    TryMoveHorizontal(1);
                    break;
                case Key.Up:
                    TryMoveVertical(-1);
                    break;
                case Key.Down:
                    TryMoveVertical(1);
                    break;
                case Key.Enter:
                case Key.Space:
                    ExecuteSelectedKey();
                    break;
                case Key.Back:
                    Backspace();
                    break;
                case Key.Escape:
                    Close();
                    break;
                default:
                    return false;
            }

            e.Handled = true;
            return true;
        }

        private void BuildUi()
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0));

            var panel = new Border
            {
                Height = 490,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(0, 2, 0, 0),
                Padding = new Thickness(34, 16, 34, 12),
                Background = GetBrushResource("OverlayMenu", new SolidColorBrush(Color.FromArgb(250, 14, 14, 22))),
                BorderBrush = GetBrushResource("FocusGameBorderBrush", new SolidColorBrush(Color.FromRgb(190, 155, 65)))
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.20, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10.0, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.20, GridUnitType.Star) });

            panel.Child = layout;
            Children.Add(panel);

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(headerGrid, 0);
            Grid.SetColumn(headerGrid, 1);
            layout.Children.Add(headerGrid);

            modeText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Opacity = 0.70,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            modeText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(modeText, 1);
            headerGrid.Children.Add(modeText);

            var inputBorder = new Border
            {
                Height = 56,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 0, 18, 0),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                BorderBrush = GetBrushResource("NoFocusBorderButtonBrush", new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)))
            };
            Grid.SetRow(inputBorder, 1);
            Grid.SetColumn(inputBorder, 1);
            layout.Children.Add(inputBorder);

            inputText = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            inputText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            inputBorder.Child = inputText;

            keysGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(keysGrid, 2);
            Grid.SetColumn(keysGrid, 1);
            layout.Children.Add(keysGrid);

            var hint = new TextBlock
            {
                Text = L("LOCInGameOverlayVirtualKeyboardHint", "A: select   X: delete   Y: space   Start: send   B: cancel"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.72,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(hint, 3);
            Grid.SetColumn(hint, 1);
            layout.Children.Add(hint);
        }

        private void RebuildKeys()
        {
            keysGrid.Children.Clear();
            keysGrid.RowDefinitions.Clear();
            buttonRows.Clear();
            buttonSpecs.Clear();

            var rows = BuildKeyRows();

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                keysGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(rowGrid, rowIndex);
                keysGrid.Children.Add(rowGrid);

                var buttonRow = new List<Button>();
                buttonRows.Add(buttonRow);

                foreach (var spec in rows[rowIndex])
                {
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(Math.Max(0.4, spec.WidthWeight), GridUnitType.Star)
                    });

                    var button = CreateKeyButton(spec);
                    Grid.SetColumn(button, buttonRow.Count);
                    rowGrid.Children.Add(button);
                    buttonRow.Add(button);
                    buttonSpecs[button] = spec;
                }
            }

            ClampSelection();
            RefreshModeText();
            UpdateSelectionVisuals();
        }

        private List<List<VirtualKeySpec>> BuildKeyRows()
        {
            var rows = new List<List<VirtualKeySpec>>();

            if (isSymbols)
            {
                if (isExtendedSymbols)
                {
                    rows.Add(BuildLiteralCharacterRow(new[] { "[", "]", "{", "}", "<", ">", ";", "^", "|", "~" }));
                    rows.Add(BuildLiteralCharacterRow(new[] { "€", "$", "£", "¥", "°", "`", "§", "µ", "¤", "±" }));
                    rows.Add(BuildLiteralCharacterRow(new[] { "©", "®", "™", "×", "÷", "√", "∞", "•", "…", "¬" }));
                }
                else
                {
                    rows.Add(BuildLiteralCharacterRow(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }));
                    rows.Add(BuildLiteralCharacterRow(new[] { "@", "#", "%", "&", "*", "+", "-", "=", "/", "\\" }));
                    rows.Add(BuildLiteralCharacterRow(new[] { ".", ",", "?", "!", "'", "\"", "_", ":", "(", ")" }));
                }
            }
            else
            {
                rows.Add(BuildPhysicalCharacterRow(PhysicalTopRowScanCodes, "QWERTYUIOP", null, 10));
                rows.Add(BuildPhysicalCharacterRow(PhysicalMiddleRowScanCodes, "ASDFGHJKL;", null, 10));
                rows.Add(BuildPhysicalCharacterRow(PhysicalBottomRowScanCodes, "ZXCVBNM,", UsefulBottomRowFillers, 10));
            }

            // The action row uses exactly ten width units, just like the character rows.
            // This keeps ABC/123 and Shift/#+= exactly the same width as a character key,
            // while the two-unit space key is mathematically centered on the keyboard.
            rows.Add(new List<VirtualKeySpec>
            {
                Special(VirtualKeyKind.ToggleSymbols, isSymbols ? "ABC" : "123", 1.00),
                isSymbols
                    ? Special(VirtualKeyKind.ToggleSymbolPage, isExtendedSymbols ? "123" : "#+=", 1.00)
                    : Special(VirtualKeyKind.ToggleCase, L("LOCInGameOverlayVirtualKeyboardShift", "Shift"), 1.00),
                Special(VirtualKeyKind.Backspace, L("LOCInGameOverlayVirtualKeyboardBackspace", "Delete"), 1.00),
                Special(VirtualKeyKind.CursorLeft, "←", 1.00),
                Special(VirtualKeyKind.Space, "␣", 2.00),
                Special(VirtualKeyKind.CursorRight, "→", 1.00),
                Special(VirtualKeyKind.Submit, L("LOCInGameOverlayVirtualKeyboardSend", "Send"), 1.00),
                Special(VirtualKeyKind.SubmitAndEnter, L("LOCInGameOverlayVirtualKeyboardSendEnter", "Send + Enter"), 2.00)
            });

            return rows;
        }

        private List<VirtualKeySpec> BuildPhysicalCharacterRow(uint[] scanCodes, string fallbackLabels, string[] extraFillers, int targetCount)
        {
            var row = new List<VirtualKeySpec>();
            var usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < scanCodes.Length; index++)
            {
                var fallback = index < fallbackLabels.Length
                    ? fallbackLabels[index].ToString()
                    : string.Empty;

                var value = GetCharacterForPhysicalKey(scanCodes[index], isUppercase, fallback);
                value = NormalizeDisplayValue(value);

                if (!IsAlphabeticKey(value) || usedValues.Contains(value))
                {
                    continue;
                }

                row.Add(new VirtualKeySpec
                {
                    Kind = VirtualKeyKind.Character,
                    Value = value,
                    Label = value,
                    WidthWeight = 1
                });

                usedValues.Add(value);
            }

            if (extraFillers != null)
            {
                foreach (var filler in extraFillers)
                {
                    if (targetCount > 0 && row.Count >= targetCount)
                    {
                        break;
                    }

                    var value = NormalizeDisplayValue(filler);
                    if (string.IsNullOrWhiteSpace(value) || usedValues.Contains(value))
                    {
                        continue;
                    }

                    row.Add(new VirtualKeySpec
                    {
                        Kind = VirtualKeyKind.Character,
                        Value = value,
                        Label = value,
                        WidthWeight = 1
                    });

                    usedValues.Add(value);
                }
            }

            return row;
        }

        private static string NormalizeDisplayValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            if (value.Length > 1)
            {
                value = value.Substring(0, 1);
            }

            return value;
        }

        private static bool IsAlphabeticKey(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.Length == 1 &&
                   char.IsLetter(value[0]);
        }

        private static bool IsAllowedAlphaModeKey(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (value.Length == 1 && char.IsLetter(value[0]))
            {
                return true;
            }

            return AlphaModePunctuation.Contains(value);
        }

        private List<VirtualKeySpec> BuildLiteralCharacterRow(string[] values)
        {
            var row = new List<VirtualKeySpec>();

            foreach (var value in values)
            {
                row.Add(new VirtualKeySpec
                {
                    Kind = VirtualKeyKind.Character,
                    Value = value,
                    Label = value,
                    WidthWeight = 1
                });
            }

            return row;
        }

        private string GetCharacterForPhysicalKey(uint scanCode, bool shiftPressed, string fallback)
        {
            try
            {
                if (activeKeyboardLayout != IntPtr.Zero)
                {
                    var keyboardState = new byte[256];
                    if (shiftPressed)
                    {
                        keyboardState[VK_SHIFT] = 0x80;
                    }

                    var virtualKey = MapVirtualKeyEx(scanCode, MAPVK_VSC_TO_VK_EX, activeKeyboardLayout);
                    if (virtualKey == 0)
                    {
                        return GetFallbackCharacter(fallback, shiftPressed);
                    }

                    var buffer = new StringBuilder(8);
                    var result = ToUnicodeEx(
                        virtualKey,
                        scanCode,
                        keyboardState,
                        buffer,
                        buffer.Capacity,
                        TO_UNICODE_NO_STATE_CHANGE,
                        activeKeyboardLayout);

                    if (result > 0)
                    {
                        var text = buffer.ToString();
                        if (text.Length > result)
                        {
                            text = text.Substring(0, result);
                        }

                        text = text.Replace("\0", string.Empty);
                        if (!string.IsNullOrEmpty(text) && !ContainsControlCharacter(text))
                        {
                            return text;
                        }
                    }
                }
            }
            catch
            {
            }

            return GetFallbackCharacter(fallback, shiftPressed);
        }

        private static bool ContainsControlCharacter(string value)
        {
            foreach (var character in value)
            {
                if (char.IsControl(character))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetFallbackCharacter(string fallback, bool shiftPressed)
        {
            if (string.IsNullOrEmpty(fallback))
            {
                return string.Empty;
            }

            return shiftPressed ? fallback.ToUpperInvariant() : fallback.ToLowerInvariant();
        }

        private IntPtr ResolveKeyboardLayout()
        {
            try
            {
                if (getKeyboardLayout != null)
                {
                    var layout = getKeyboardLayout();
                    if (layout != IntPtr.Zero)
                    {
                        return layout;
                    }
                }
            }
            catch
            {
            }

            return GetKeyboardLayout(0);
        }

        private static VirtualKeySpec Special(VirtualKeyKind kind, string label, double widthWeight)
        {
            return new VirtualKeySpec
            {
                Kind = kind,
                Label = label,
                Value = string.Empty,
                WidthWeight = widthWeight
            };
        }

        private Button CreateKeyButton(VirtualKeySpec spec)
        {
            var button = new Button
            {
                Content = spec.Label,
                Height = 46,
                Margin = new Thickness(4, 3, 4, 3),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = GetKeyFontSize(spec.Kind),
                FontWeight = GetKeyFontWeight(spec.Kind),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Focusable = false,
                IsTabStop = false,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Template = CreateKeyButtonTemplate(),
                Background = CreateIdleKeyBackground(),
                BorderBrush = CreateIdleKeyBorder(),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(245, 241, 234))
            };

            if (IsCursorKey(spec.Kind) || spec.Kind == VirtualKeyKind.Space)
            {
                button.FontFamily = new FontFamily("Segoe UI Symbol");
            }

            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            button.Click += (s, e) => ExecuteKey(spec);
            button.MouseEnter += (s, e) => SelectButton(button);

            return button;
        }

        private static Brush CreateIdleKeyBackground()
        {
            return new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
        }

        private static Brush CreateIdleKeyBorder()
        {
            return new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        }

        private static double GetKeyFontSize(VirtualKeyKind kind)
        {
            switch (kind)
            {
                case VirtualKeyKind.Character:
                    return 22;
                case VirtualKeyKind.CursorLeft:
                case VirtualKeyKind.CursorRight:
                    return 26;
                case VirtualKeyKind.Space:
                    return 27;
                case VirtualKeyKind.Submit:
                case VirtualKeyKind.SubmitAndEnter:
                case VirtualKeyKind.Backspace:
                case VirtualKeyKind.ToggleCase:
                case VirtualKeyKind.ToggleSymbols:
                case VirtualKeyKind.ToggleSymbolPage:
                    return 18;
                default:
                    return 16;
            }
        }

        private static FontWeight GetKeyFontWeight(VirtualKeyKind kind)
        {
            switch (kind)
            {
                case VirtualKeyKind.CursorLeft:
                case VirtualKeyKind.CursorRight:
                case VirtualKeyKind.Submit:
                case VirtualKeyKind.SubmitAndEnter:
                    return FontWeights.Bold;
                default:
                    return FontWeights.SemiBold;
            }
        }

        private static bool IsCursorKey(VirtualKeyKind kind)
        {
            return kind == VirtualKeyKind.CursorLeft || kind == VirtualKeyKind.CursorRight;
        }

        private static ControlTemplate CreateKeyButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
            });

            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private void SelectButton(Button button)
        {
            for (var row = 0; row < buttonRows.Count; row++)
            {
                var column = buttonRows[row].IndexOf(button);
                if (column < 0)
                {
                    continue;
                }

                selectedRow = row;
                selectedColumn = column;
                UpdateSelectionVisuals();
                return;
            }
        }

        private void TryMoveHorizontal(int direction)
        {
            if (IsDuplicateDirectionalInput(direction, 0))
            {
                return;
            }

            MoveHorizontal(direction);
        }

        private void TryMoveVertical(int direction)
        {
            if (IsDuplicateDirectionalInput(0, direction))
            {
                return;
            }

            MoveVertical(direction);
        }

        private bool IsDuplicateDirectionalInput(int horizontal, int vertical)
        {
            var now = DateTime.UtcNow;
            var isDuplicate =
                horizontal == lastDirectionalHorizontal &&
                vertical == lastDirectionalVertical &&
                (now - lastDirectionalInputTime).TotalMilliseconds < DirectionalDuplicateWindowMs;

            if (isDuplicate)
            {
                return true;
            }

            lastDirectionalHorizontal = horizontal;
            lastDirectionalVertical = vertical;
            lastDirectionalInputTime = now;
            return false;
        }

        private void MoveHorizontal(int direction)
        {
            if (buttonRows.Count == 0)
            {
                return;
            }

            ClampSelection();
            var row = buttonRows[selectedRow];
            selectedColumn = (selectedColumn + direction + row.Count) % row.Count;
            UpdateSelectionVisuals();
        }

        private void MoveVertical(int direction)
        {
            if (buttonRows.Count == 0)
            {
                return;
            }

            ClampSelection();

            var oldRow = buttonRows[selectedRow];
            var normalizedPosition = oldRow.Count <= 1
                ? 0.0
                : (double)selectedColumn / (oldRow.Count - 1);

            selectedRow = (selectedRow + direction + buttonRows.Count) % buttonRows.Count;

            var newRow = buttonRows[selectedRow];
            selectedColumn = newRow.Count <= 1
                ? 0
                : (int)Math.Round(normalizedPosition * (newRow.Count - 1));

            ClampSelection();
            UpdateSelectionVisuals();
        }

        private void ClampSelection()
        {
            if (buttonRows.Count == 0)
            {
                selectedRow = 0;
                selectedColumn = 0;
                return;
            }

            selectedRow = Math.Max(0, Math.Min(selectedRow, buttonRows.Count - 1));

            var row = buttonRows[selectedRow];
            if (row.Count == 0)
            {
                selectedColumn = 0;
                return;
            }

            selectedColumn = Math.Max(0, Math.Min(selectedColumn, row.Count - 1));
        }

        private void ExecuteSelectedKey()
        {
            ClampSelection();

            if (buttonRows.Count == 0 || buttonRows[selectedRow].Count == 0)
            {
                return;
            }

            var button = buttonRows[selectedRow][selectedColumn];
            VirtualKeySpec spec;

            if (buttonSpecs.TryGetValue(button, out spec))
            {
                ExecuteKey(spec);
            }
        }

        private void ExecuteKey(VirtualKeySpec spec)
        {
            if (spec == null)
            {
                return;
            }

            switch (spec.Kind)
            {
                case VirtualKeyKind.Character:
                    AppendText(spec.Value);
                    break;
                case VirtualKeyKind.ToggleSymbols:
                    isSymbols = !isSymbols;
                    isExtendedSymbols = false;
                    selectedRow = 0;
                    selectedColumn = 0;
                    RebuildKeys();
                    break;
                case VirtualKeyKind.ToggleCase:
                    isUppercase = !isUppercase;
                    RebuildKeys();
                    break;
                case VirtualKeyKind.ToggleSymbolPage:
                    isExtendedSymbols = !isExtendedSymbols;
                    selectedRow = 0;
                    selectedColumn = 0;
                    RebuildKeys();
                    break;
                case VirtualKeyKind.Space:
                    AppendText(" ");
                    break;
                case VirtualKeyKind.Backspace:
                    Backspace();
                    break;
                case VirtualKeyKind.CursorLeft:
                    MoveTextCursor(-1);
                    break;
                case VirtualKeyKind.CursorRight:
                    MoveTextCursor(1);
                    break;
                case VirtualKeyKind.Submit:
                    Submit(false);
                    break;
                case VirtualKeyKind.SubmitAndEnter:
                    Submit(true);
                    break;
                case VirtualKeyKind.Cancel:
                    Close();
                    break;
            }
        }

        private void AppendText(string value)
        {
            if (string.IsNullOrEmpty(value) || currentText.Length >= 160)
            {
                return;
            }

            var remainingLength = 160 - currentText.Length;
            if (value.Length > remainingLength)
            {
                value = value.Substring(0, remainingLength);
            }

            cursorPosition = Math.Max(0, Math.Min(cursorPosition, currentText.Length));
            currentText = currentText.Insert(cursorPosition, value);
            cursorPosition += value.Length;
            RefreshInputText();
        }

        private void Backspace()
        {
            cursorPosition = Math.Max(0, Math.Min(cursorPosition, currentText.Length));

            if (cursorPosition == 0 || currentText.Length == 0)
            {
                return;
            }

            currentText = currentText.Remove(cursorPosition - 1, 1);
            cursorPosition--;
            RefreshInputText();
        }

        private void MoveTextCursor(int direction)
        {
            cursorPosition = Math.Max(0, Math.Min(currentText.Length, cursorPosition + direction));
            RefreshInputText();
        }

        private void Submit(bool pressEnter)
        {
            var text = currentText;
            Visibility = Visibility.Collapsed;
            submitAction?.Invoke(text, pressEnter);
        }

        private void RefreshInputText()
        {
            if (inputText == null)
            {
                return;
            }

            cursorPosition = Math.Max(0, Math.Min(cursorPosition, currentText.Length));

            inputText.Text = string.IsNullOrEmpty(currentText)
                ? L("LOCInGameOverlayVirtualKeyboardPlaceholder", "Enter text...")
                : currentText.Insert(cursorPosition, "|");

            inputText.Opacity = string.IsNullOrEmpty(currentText) ? 0.45 : 1.0;
        }

        private void RefreshModeText()
        {
            if (modeText == null)
            {
                return;
            }

            var layoutName = isSymbols
                ? (isExtendedSymbols ? "#+=" : "123")
                : (isUppercase ? "ABC" : "abc");
            modeText.Text = L("LOCInGameOverlayVirtualKeyboardMode", "Layout") + ": " + layoutName;
        }

        private void UpdateSelectionVisuals()
        {
            for (var row = 0; row < buttonRows.Count; row++)
            {
                for (var column = 0; column < buttonRows[row].Count; column++)
                {
                    var button = buttonRows[row][column];
                    VirtualKeySpec spec;
                    buttonSpecs.TryGetValue(button, out spec);

                    var isSelected = row == selectedRow && column == selectedColumn;
                    var isShiftActive = isUppercase && spec != null && spec.Kind == VirtualKeyKind.ToggleCase;

                    if (isSelected)
                    {
                        button.SetResourceReference(Control.BackgroundProperty, "ButtonBackgroundFocus");
                        button.SetResourceReference(Control.BorderBrushProperty, "FocusGameBorderBrush");
                        button.BorderThickness = new Thickness(2);
                    }
                    else if (isShiftActive)
                    {
                        button.SetResourceReference(Control.BackgroundProperty, "ButtonBackgroundFocus");
                        button.SetResourceReference(Control.BorderBrushProperty, "FocusGameBorderBrush");
                        button.BorderThickness = new Thickness(1);
                    }
                    else
                    {
                        button.Background = CreateIdleKeyBackground();
                        button.BorderBrush = CreateIdleKeyBorder();
                        button.BorderThickness = new Thickness(1);
                    }
                }
            }
        }

        private string L(string key, string fallback)
        {
            try
            {
                return loc != null ? loc(key, fallback) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private Brush GetBrushResource(string key, Brush fallback)
        {
            try
            {
                var value = TryFindResource(key) as Brush;
                return value ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(
            uint virtualKey,
            uint scanCode,
            byte[] keyboardState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer,
            int bufferSize,
            uint flags,
            IntPtr keyboardLayout);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);
    }
}
