using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AgentSpace.Core.Services;
using AgentSpace.Core.Interop;

namespace AgentSpace.App.Views
{
    public partial class IntentRoutingOverlayWindow : Window
    {
        private readonly ContainerWindow _sourceWindow;
        private readonly List<IntentRouterService.TargetWindowInfo> _availableTargets;
        private readonly IntentRouterService _routerService;
        private int _currentTargetIndex = 0;

        public IntentRoutingOverlayWindow(ContainerWindow sourceWindow, IntentRouterService routerService)
        {
            InitializeComponent();
            _sourceWindow = sourceWindow;
            _routerService = routerService;

            // Find all other open windows using the new EnumWindows capability
            _availableTargets = _routerService.GetAvailableTargetWindows(_sourceWindow.TargetHwnd);

            if (!_availableTargets.Any())
            {
                TargetHighlightBorder.Visibility = Visibility.Hidden;
                TargetIndexText.Visibility = Visibility.Hidden;
                HudContainerBorder.Visibility = Visibility.Hidden;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_availableTargets.Any())
            {
                TargetsListBox.ItemsSource = _availableTargets.Select(t => t.Title).ToList();
                
                HudContainerBorder.Visibility = Visibility.Visible;
                HudContainerBorder.Measure(new Size(this.ActualWidth, this.ActualHeight));
                Canvas.SetLeft(HudContainerBorder, (this.ActualWidth - HudContainerBorder.DesiredSize.Width) / 2);
                Canvas.SetTop(HudContainerBorder, (this.ActualHeight - HudContainerBorder.DesiredSize.Height) / 2);
            }

            UpdateHighlight();
            
            // Re-focus this overlay window to ensure it receives Tab and Space keys
            this.Activate();
            this.Focus();
            Keyboard.Focus(this);
            var hwnd = new WindowInteropHelper(this).Handle;
            User32.SetForegroundWindow(hwnd);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
                e.Handled = true;
                return;
            }

            if (!_availableTargets.Any()) return;

            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                _availableTargets.RemoveAt(_currentTargetIndex);
                if (!_availableTargets.Any())
                {
                    this.Close();
                }
                else
                {
                    if (_currentTargetIndex >= _availableTargets.Count)
                    {
                        _currentTargetIndex = 0;
                    }
                    TargetsListBox.ItemsSource = _availableTargets.Select(t => t.Title).ToList();
                    UpdateHighlight();
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                // Shift+Tab for reverse
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    _currentTargetIndex = (_currentTargetIndex - 1 + _availableTargets.Count) % _availableTargets.Count;
                }
                else
                {
                    _currentTargetIndex = (_currentTargetIndex + 1) % _availableTargets.Count;
                }
                UpdateHighlight();
                e.Handled = true;
            }
            else if (e.Key == Key.Space || e.Key == Key.Enter)
            {
                ExecuteRoutingAsync();
                e.Handled = true;
            }
        }

        private void UpdateHighlight()
        {
            if (!_availableTargets.Any()) return;

            var target = _availableTargets[_currentTargetIndex];
            
            // DPI scaling
            var source = PresentationSource.FromVisual(this);
            double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            // Convert physical pixels to WPF DIPs
            double dipLeft = target.Bounds.Left * scaleX;
            double dipTop = target.Bounds.Top * scaleY;
            double dipWidth = (target.Bounds.Right - target.Bounds.Left) * scaleX;
            double dipHeight = (target.Bounds.Bottom - target.Bounds.Top) * scaleY;
            
            TargetHighlightBorder.Width = dipWidth;
            TargetHighlightBorder.Height = dipHeight;
            Canvas.SetLeft(TargetHighlightBorder, dipLeft);
            Canvas.SetTop(TargetHighlightBorder, dipTop);
            
            Canvas.SetLeft(TargetIndexText, dipLeft);
            Canvas.SetTop(TargetIndexText, dipTop - 25);
            
            string displayTitle = target.Title;
            if (displayTitle.Length > 40) displayTitle = displayTitle.Substring(0, 37) + "...";
            
            TargetIndexText.Text = $"[{_currentTargetIndex + 1}/{_availableTargets.Count}] {displayTitle}";
            TargetHighlightBorder.Visibility = Visibility.Visible;
            TargetIndexText.Visibility = Visibility.Visible;
            
            if (TargetsListBox.Items.Count > _currentTargetIndex)
            {
                TargetsListBox.SelectedIndex = _currentTargetIndex;
                TargetsListBox.ScrollIntoView(TargetsListBox.SelectedItem);
            }
        }

        private async void ExecuteRoutingAsync()
        {
            // Hide the overlay so it doesn't block interactions
            this.Visibility = Visibility.Hidden;

            var target = _availableTargets[_currentTargetIndex];
            
            // Execute the copy-paste routine through the service
            bool success = await _routerService.RouteIntentAsync(_sourceWindow.TargetHwnd, target.Hwnd);
            
            if (!success)
            {
                MessageBox.Show("Failed to route intent. Make sure the source had text selected, and the target accepts input.", "Routing Failed");
            }
            
            this.Close();
        }
    }
}
