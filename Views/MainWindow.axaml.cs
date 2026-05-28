using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using SecureTokenTool.ViewModels;

namespace SecureTokenTool.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as MainWindowViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    /// <summary>ログ更新時にテキストボックスを末尾までスクロールする。</summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Log))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });
    }
}
