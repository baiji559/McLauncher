using System.Windows.Input;

namespace McLauncher.App;

public abstract class LauncherCommand(Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public abstract void Execute(object? parameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : LauncherCommand(canExecute)
{
    public override async void Execute(object? parameter) => await execute();
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : LauncherCommand(canExecute)
{
    public override void Execute(object? parameter) => execute();
}
