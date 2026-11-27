using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class SnapshotViewerWindow : EzFluentWindow
{
    private readonly SnapshotViewerViewModel _viewModel;
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHOffset;
    private double _panStartVOffset;

    public SnapshotViewerWindow(SnapshotViewerViewModel viewModel, AppearanceSettingsService appearance)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);

        _viewModel.FitToWindowRequested += (_, _) =>
            Dispatcher.BeginInvoke(FitNow, DispatcherPriority.Loaded);

        Loaded += async (_, _) =>
        {
            appearance.ApplyToWindow(this);
            await _viewModel.LoadCurrentAsync();
            FitNow();
        };
    }

    private void FitNow()
    {
        if (_viewModel.CurrentImage is not BitmapSource bmp)
        {
            return;
        }

        double imgW = bmp.Width > 0 ? bmp.Width : bmp.PixelWidth;
        double imgH = bmp.Height > 0 ? bmp.Height : bmp.PixelHeight;
        _viewModel.ZoomPercent = SnapshotViewerViewModel.ComputeFitZoom(
            ImageScroller.ViewportWidth - 8,
            ImageScroller.ViewportHeight - 8,
            imgW,
            imgH);
    }

    private void ImageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
        {
            _viewModel.ZoomInCommand.Execute(null);
        }
        else if (e.Delta < 0)
        {
            _viewModel.ZoomOutCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void ViewerImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (Math.Abs(_viewModel.ZoomPercent - 100.0) < 1.0)
            {
                FitNow();
            }
            else
            {
                _viewModel.ResetZoomCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStartPoint = e.GetPosition(ImageScroller);
        _panStartHOffset = ImageScroller.HorizontalOffset;
        _panStartVOffset = ImageScroller.VerticalOffset;
        ViewerImage.CaptureMouse();
    }

    private void ViewerImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        Point current = e.GetPosition(ImageScroller);
        ImageScroller.ScrollToHorizontalOffset(_panStartHOffset - (current.X - _panStartPoint.X));
        ImageScroller.ScrollToVerticalOffset(_panStartVOffset - (current.Y - _panStartPoint.Y));
    }

    private void ViewerImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ViewerImage.ReleaseMouseCapture();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                _viewModel.ZoomInCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                _viewModel.ZoomOutCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                _viewModel.ResetZoomCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
                FitNow();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}
