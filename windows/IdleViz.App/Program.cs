using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace IdleViz.App;

public static class Program
{
    private const string InstanceKey = "IdleViz";

    /// <summary>Raised in the running copy when another copy was started and handed its command line over.</summary>
    internal static event Action<LaunchOptions>? Relaunched;

    [STAThread]
    private static void Main(string[] arguments)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // One running copy only. A second launch, `start idleviz://open` included, hands over to the first.
        var first = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!first.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Not awaited on this thread: it is the UI thread type (STA), where blocking on the call can hang.
            Task.Run(() => first.RedirectActivationToAsync(activation).AsTask()).Wait();
            return;
        }

        first.Activated += (_, activation) => Relaunched?.Invoke(OptionsFrom(activation));

        var options = LaunchOptions.Parse(arguments);
        Application.Start(start =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options);
        });
    }

    private static LaunchOptions OptionsFrom(AppActivationArguments activation)
    {
        // An unpackaged app gets a second copy's whole command line as one string.
        if (activation.Data is ILaunchActivatedEventArgs launch)
        {
            return LaunchOptions.Parse(LaunchOptions.SplitCommandLine(launch.Arguments));
        }

        return activation.Data is IProtocolActivatedEventArgs protocol
            ? LaunchOptions.Parse([protocol.Uri.OriginalString])
            : new LaunchOptions();
    }
}
