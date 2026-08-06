using System;
using System.Windows.Input;

namespace Syncr.UI.ViewModels
{
    public class SimpleCommand : ICommand
    {
        private readonly Action _action;

        public SimpleCommand(Action action)
        {
            _action = action;
        }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _action();
    }

    public class SimpleCommand<T> : ICommand
    {
        private readonly Action<T> _action;

        public SimpleCommand(Action<T> action)
        {
            _action = action;
        }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (parameter is T t) _action(t);
            else if (parameter == null) _action(default!);
        }
    }
}
