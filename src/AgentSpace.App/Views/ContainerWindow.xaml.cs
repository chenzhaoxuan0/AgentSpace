using System;
using System.Windows;
using System.Windows.Input;
using AgentSpace.Core.Models;
using AgentSpace.Core.Services;
using System.Threading.Tasks;

namespace AgentSpace.App.Views
{
    public partial class ContainerWindow : Window
    {
        private readonly IntPtr _targetHwnd;
        private readonly WindowMaskingService _maskingService;
        private readonly IntentRouterService _routerService;
        public IntPtr TargetHwnd => _targetHwnd;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;
        
        // We temporarily disable sync when we first position the window programmatically
        private bool _isSyncEnabled = false;

        public ContainerWindow(IntPtr targetHwnd, WindowMaskingService maskingService, Rect initialScreenBounds)
        {
            InitializeComponent();
            _targetHwnd = targetHwnd;
            _maskingService = maskingService;
            _routerService = new IntentRouterService();

            // Apply starting coordinates and size
            this.Left = initialScreenBounds.Left;
            this.Top = initialScreenBounds.Top;
            this.Width = initialScreenBounds.Width;
            this.Height = initialScreenBounds.Height;

            this.Loaded += (s, e) => _isSyncEnabled = true;
            _maskingService.NativeWindowBoundsChanged += MaskingService_NativeWindowBoundsChanged;
            _maskingService.NativeWindowMinimized += MaskingService_NativeWindowMinimized;
            _maskingService.NativeWindowRestored += MaskingService_NativeWindowRestored;
        }

        private bool _isUpdatingMask = false;

        private void MaskingService_NativeWindowMinimized(IntPtr hwnd)
        {
            if (hwnd == _targetHwnd)
            {
                Application.Current.Dispatcher.InvokeAsync(() => this.Hide());
            }
        }

        private async void MaskingService_NativeWindowRestored(IntPtr hwnd)
        {
            if (hwnd == _targetHwnd)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                    // Wait for the Windows DWM restore animation to finish before re-applying the mask
                    await Task.Delay(50);
                    this.Show();
                    UpdateNativeMaskFromWpfSize(true);
                });
            }
        }

        private void MaskingService_NativeWindowBoundsChanged(IntPtr hwnd)
        {
            if (hwnd == _targetHwnd && _isSyncEnabled && !_isUpdatingMask)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!_isSyncEnabled || _isUpdatingMask) return;

                    _isUpdatingMask = true;
                    UpdateNativeMaskFromWpfSize();
                    _isUpdatingMask = false;
                });
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                // To get actual physical pixels, we multiply the DIPs by the Device Transform
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (!_isSyncEnabled || _targetHwnd == IntPtr.Zero || _isResizing) return;

            // Convert WPF's DIPs location to physical pixels for Win32 API
            int physLeft = (int)(this.Left * _dpiScaleX);
            int physTop = (int)(this.Top * _dpiScaleY);
            
            _maskingService.SyncWindowPosition(_targetHwnd, physLeft, physTop);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
             if (!_isSyncEnabled || _targetHwnd == IntPtr.Zero || _isResizing) return;

             // Only trigger mask update when user resizes, not during initialization
             if (e.PreviousSize.Width > 0 && e.PreviousSize.Height > 0)
             {
                  UpdateNativeMaskFromWpfSize();
             }
        }

        private bool _isResizing = false;

        private void Thumb_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            if (AppSettings.ShowFullWindowDuringResize)
            {
                _isResizing = true;
                _maskingService.ClearMaskRegion(_targetHwnd);
            }
        }

        private void Thumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (AppSettings.ShowFullWindowDuringResize)
            {
                _isResizing = false;
                UpdateNativeMaskFromWpfSize(true);
            }
        }

        private void Thumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var thumb = sender as System.Windows.Controls.Primitives.Thumb;
            if (thumb == null) return;

            double left = this.Left;
            double top = this.Top;
            double width = this.Width;
            double height = this.Height;

            if (thumb.Name.Contains("W"))
            {
                double delta = Math.Min(e.HorizontalChange, width - 20);
                left += delta;
                width -= delta;
            }
            else if (thumb.Name.Contains("E"))
            {
                width = Math.Max(20, width + e.HorizontalChange);
            }

            if (thumb.Name.Contains("N"))
            {
                double delta = Math.Min(e.VerticalChange, height - 20);
                top += delta;
                height -= delta;
            }
            else if (thumb.Name.Contains("S"))
            {
                height = Math.Max(20, height + e.VerticalChange);
            }

            // Temporarily disable LocationChanged sync to avoid double-firing Win32 APIs during drag
            bool oldSync = _isSyncEnabled;
            _isSyncEnabled = false;

            this.Left = left;
            this.Top = top;
            this.Width = width;
            this.Height = height;

            _isSyncEnabled = oldSync;

            if (!_isResizing)
            {
                // Trigger a manual sync now that dimensions are updated
                UpdateNativeMaskFromWpfSize();
                Window_LocationChanged(this, EventArgs.Empty);
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_isSyncEnabled || _targetHwnd == IntPtr.Zero) return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                double scaleMultiplier = e.Delta > 0 ? 1.1 : 0.9;
                
                // Scale the native window physically
                _maskingService.ScaleNativeWindow(_targetHwnd, scaleMultiplier);
                
                // Re-sync the new mask relative coordinates to fit the current WPF container size
                var dummySizeInfo = new SizeChangedInfo(this, new Size(this.Width, this.Height), true, true);
                
                // In WPF we can't easily fake the protected SizeChangedEventArgs constructor 
                // We extract the internal logic into a helper method
                UpdateNativeMaskFromWpfSize();

                e.Handled = true;
            }
        }

        private void UpdateNativeMaskFromWpfSize(bool forceUpdate = false)
        {
            if (!_isSyncEnabled || _targetHwnd == IntPtr.Zero) return;

             int physLeft = (int)(this.Left * _dpiScaleX);
             int physTop = (int)(this.Top * _dpiScaleY);
             int physRight = physLeft + (int)(this.Width * _dpiScaleX);
             int physBottom = physTop + (int)(this.Height * _dpiScaleY);

             var newScreenRect = new NativeRect 
             {
                 Left = physLeft,
                 Top = physTop,
                 Right = physRight,
                 Bottom = physBottom
             };

             _maskingService.UpdateMaskRegion(_targetHwnd, newScreenRect, forceUpdate);
        }

        private void RouteIntentButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerRouteIntent();
        }

        public void TriggerRouteIntent()
        {
            // Spawn the fullscreen targeting overlay 
            var overlay = new IntentRoutingOverlayWindow(this, _routerService);
            overlay.ShowDialog();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _maskingService.NativeWindowBoundsChanged -= MaskingService_NativeWindowBoundsChanged;
            _maskingService.NativeWindowMinimized -= MaskingService_NativeWindowMinimized;
            _maskingService.NativeWindowRestored -= MaskingService_NativeWindowRestored;
            _maskingService.RestoreMaskedWindow(_targetHwnd);
            base.OnClosed(e);
        }
    }
}
