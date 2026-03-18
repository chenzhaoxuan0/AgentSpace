using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentSpace.Core.Models;

namespace AgentSpace.App.Views
{
    public partial class SelectionOverlayWindow : Window
    {
        private Point _startPoint;
        private bool _isDrawing = false;
        private bool _isDraggingBox = false;
        private Point _dragOffset;
        private bool _hasDrawnBox = false;

        public event Action<NativeRect> SelectionCompleted;

        public SelectionOverlayWindow()
        {
            InitializeComponent();
        }

        private void OverlayCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (_hasDrawnBox)
                {
                    _hasDrawnBox = false;
                }

                _isDrawing = true;
                _startPoint = e.GetPosition(OverlayCanvas);
                
                SelectionGrid.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionGrid, _startPoint.X);
                Canvas.SetTop(SelectionGrid, _startPoint.Y);
                SelectionGrid.Width = 0;
                SelectionGrid.Height = 0;
                
                OverlayCanvas.CaptureMouse();
            }
        }

        private void SelectionGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _hasDrawnBox && !ControlPanel.IsMouseOver)
            {
                _isDraggingBox = true;
                _dragOffset = e.GetPosition(SelectionGrid);
                OverlayCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawing)
            {
                Point currentPoint = e.GetPosition(OverlayCanvas);
                
                double x = Math.Min(_startPoint.X, currentPoint.X);
                double y = Math.Min(_startPoint.Y, currentPoint.Y);
                double width = Math.Abs(_startPoint.X - currentPoint.X);
                double height = Math.Abs(_startPoint.Y - currentPoint.Y);

                Canvas.SetLeft(SelectionGrid, x);
                Canvas.SetTop(SelectionGrid, y);
                SelectionGrid.Width = width;
                SelectionGrid.Height = height;
            }
            else if (_isDraggingBox)
            {
                Point currentPoint = e.GetPosition(OverlayCanvas);
                Canvas.SetLeft(SelectionGrid, currentPoint.X - _dragOffset.X);
                Canvas.SetTop(SelectionGrid, currentPoint.Y - _dragOffset.Y);
            }
        }

        private void OverlayCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawing && e.LeftButton == MouseButtonState.Released)
            {
                _isDrawing = false;
                _hasDrawnBox = true;
                OverlayCanvas.ReleaseMouseCapture();
            }
            else if (_isDraggingBox && e.LeftButton == MouseButtonState.Released)
            {
                _isDraggingBox = false;
                OverlayCanvas.ReleaseMouseCapture();
            }
        }

        private void Thumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var thumb = sender as System.Windows.Controls.Primitives.Thumb;
            if (thumb == null) return;

            double left = Canvas.GetLeft(SelectionGrid);
            double top = Canvas.GetTop(SelectionGrid);
            double width = SelectionGrid.Width;
            double height = SelectionGrid.Height;

            if (thumb.Name.Contains("W"))
            {
                double delta = Math.Min(e.HorizontalChange, width - 10);
                left += delta;
                width -= delta;
            }
            else if (thumb.Name.Contains("E"))
            {
                width = Math.Max(10, width + e.HorizontalChange);
            }

            if (thumb.Name.Contains("N"))
            {
                double delta = Math.Min(e.VerticalChange, height - 10);
                top += delta;
                height -= delta;
            }
            else if (thumb.Name.Contains("S"))
            {
                height = Math.Max(10, height + e.VerticalChange);
            }

            Canvas.SetLeft(SelectionGrid, left);
            Canvas.SetTop(SelectionGrid, top);
            SelectionGrid.Width = width;
            SelectionGrid.Height = height;
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSelection();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ResetAndHide();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isDrawing || _isDraggingBox)
                {
                    _isDrawing = false;
                    _isDraggingBox = false;
                    OverlayCanvas.ReleaseMouseCapture();
                }
                ResetAndHide();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _hasDrawnBox)
            {
                CommitSelection();
            }
        }

        private void CommitSelection()
        {
            if (!_hasDrawnBox) return;

            Point topLeft = new Point(Canvas.GetLeft(SelectionGrid), Canvas.GetTop(SelectionGrid));
            Point bottomRight = new Point(topLeft.X + SelectionGrid.Width, topLeft.Y + SelectionGrid.Height);
            
            Point screenTopLeft = OverlayCanvas.PointToScreen(topLeft);
            Point screenBottomRight = OverlayCanvas.PointToScreen(bottomRight);

            var nativeRect = new NativeRect 
            {
                Left = (int)screenTopLeft.X,
                Top = (int)screenTopLeft.Y,
                Right = (int)screenBottomRight.X,
                Bottom = (int)screenBottomRight.Y
            };
            
            ResetAndHide();
            SelectionCompleted?.Invoke(nativeRect);
        }

        private void ResetAndHide()
        {
            SelectionGrid.Visibility = Visibility.Hidden;
            _hasDrawnBox = false;
            this.Hide(); 
        }
    }
}
