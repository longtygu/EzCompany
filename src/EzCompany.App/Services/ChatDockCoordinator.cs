using System.Windows;
using System.Windows.Controls;
using EzCompany.App.ViewModels;
using EzCompany.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Services;

/// <summary>
/// Coordinates detaching the internal chat panel into an independent window and
/// seamlessly redocking the exact live chat instance back into <see cref="WorkspaceWindow"/>.
/// </summary>
public sealed class ChatDockCoordinator
{
    private readonly IServiceProvider _services;
    private DetachedChatWindow? _detachedWindow;
    private ChatWorkspaceView? _chatView;
    private ChatWorkspaceViewModel? _chatViewModel;
    private bool _isTransitioning;

    public ChatDockCoordinator(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsChatDetached { get; private set; }
    public ChatWorkspaceView? DetachedChatView => _chatView;

    public event EventHandler<bool>? DetachedStateChanged;
    public event EventHandler<bool>? DockDropHighlightChanged;
    public event EventHandler<ChatWorkspaceView?>? RedockedToMainRequested;

    public void DetachChat(ChatWorkspaceView chatView, ChatWorkspaceViewModel viewModel, Window hostWindow)
    {
        ArgumentNullException.ThrowIfNull(chatView);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(hostWindow);

        if (_detachedWindow is not null)
        {
            if (_detachedWindow.WindowState == WindowState.Minimized)
            {
                _detachedWindow.WindowState = WindowState.Normal;
            }
            _detachedWindow.Activate();
            return;
        }

        _isTransitioning = true;
        try
        {
            _chatView = chatView;
            _chatViewModel = viewModel;
            chatView.SuppressDisposeOnUnload = true;
            viewModel.IsDetachedWindow = true;
            IsChatDetached = true;

            DetachedStateChanged?.Invoke(this, true);

            if (chatView.Parent is ContentControl oldParent)
            {
                oldParent.Content = null;
            }
            else if (chatView.Parent is ContentPresenter presenter)
            {
                presenter.Content = null;
            }

            DetachedChatWindow win = _services.GetRequiredService<DetachedChatWindow>();
            win.AttachChat(chatView, hostWindow, this);
            _detachedWindow = win;
            win.Show();
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public void SetDropHighlight(bool isHighlighted) =>
        DockDropHighlightChanged?.Invoke(this, isHighlighted);

    public void RedockToMain()
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;
        try
        {
            SetDropHighlight(false);
            ChatWorkspaceView? view = _chatView;
            if (view is not null)
            {
                view.SuppressDisposeOnUnload = true;
            }

            DetachedChatWindow? win = _detachedWindow;
            _detachedWindow = null;

            if (win is not null)
            {
                win.DetachContentForRedock();
                win.Close();
            }

            if (_chatViewModel is not null)
            {
                _chatViewModel.IsDetachedWindow = false;
            }

            IsChatDetached = false;
            _chatView = null;
            _chatViewModel = null;

            DetachedStateChanged?.Invoke(this, false);
            RedockedToMainRequested?.Invoke(this, view);
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public void NotifyDetachedWindowClosing()
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;
        try
        {
            SetDropHighlight(false);
            ChatWorkspaceView? view = _chatView;
            if (view is not null)
            {
                view.SuppressDisposeOnUnload = true;
            }

            _detachedWindow?.DetachContentForRedock();
            _detachedWindow = null;

            if (_chatViewModel is not null)
            {
                _chatViewModel.IsDetachedWindow = false;
            }

            IsChatDetached = false;
            _chatView = null;
            _chatViewModel = null;

            DetachedStateChanged?.Invoke(this, false);
            RedockedToMainRequested?.Invoke(this, view);
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public void CloseDetachedWindowOnShutdown()
    {
        _isTransitioning = true;
        try
        {
            SetDropHighlight(false);
            if (_chatView is not null)
            {
                _chatView.SuppressDisposeOnUnload = false;
            }

            if (_detachedWindow is not null)
            {
                _detachedWindow.Close();
                _detachedWindow = null;
            }

            IsChatDetached = false;
            _chatView = null;
            _chatViewModel = null;
        }
        finally
        {
            _isTransitioning = false;
        }
    }
}
