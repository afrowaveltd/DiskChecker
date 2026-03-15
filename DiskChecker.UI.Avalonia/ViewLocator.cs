using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DiskChecker.UI.Avalonia.ViewModels;

namespace DiskChecker.UI.Avalonia;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!;

        // Convert namespace from ViewModels to Views and class name from ViewModel to View
        name = name.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);
        name = name.Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = typeof(ViewLocator).Assembly.GetType(name, throwOnError: false, ignoreCase: false);
        if (type is null)
        {
            return new TextBlock { Text = "Not Found: " + name };
        }

        try
        {
            if (Activator.CreateInstance(type) is Control control)
            {
                return control;
            }

            return new TextBlock { Text = "Invalid View Type: " + name };
        }
        catch (InvalidOperationException ex)
        {
            return new TextBlock { Text = $"View init error: {name} ({ex.Message})" };
        }
        catch (MemberAccessException ex)
        {
            return new TextBlock { Text = $"View access error: {name} ({ex.Message})" };
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            return new TextBlock { Text = $"View constructor error: {name} ({inner})" };
        }
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}