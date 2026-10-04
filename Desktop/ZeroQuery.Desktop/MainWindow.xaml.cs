using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Zeroquery.Core.Audit;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Mutations;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Core.Security.DataProtection;
using ZeroQuery.Desktop.Bridge;
using ZeroQuery.Desktop.Services;

namespace ZeroQuery.Desktop;

public sealed partial class MainWindow : Window
{
    private DesktopIpcDispatcher? _dispatcher;

    public MainWindow()
    {
        InitializeComponent();
        this.Title = "⚡ ZeroQuery Desktop";

        // Configure default window dimensions matching the reference mock design (1200x820)
        try
        {
            this.AppWindow.Resize(new SizeInt32(1200, 820));

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.targetsize-24_altform-unplated.png");
            if (File.Exists(iconPath))
            {
                this.AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Fallback for older platform builds
        }

        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        await AppWebView.EnsureCoreWebView2Async();

        AppWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        AppWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        AppWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        // Resolve wwwroot folder
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(wwwroot))
        {
            // Dev-time fallback if launched directly without copy
            var devWwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
            if (Directory.Exists(devWwwroot))
            {
                wwwroot = devWwwroot;
            }
        }

        // Map virtual hostname to local folder: zero HTTP servers or network ports
        AppWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "zeroquery.local",
            wwwroot,
            CoreWebView2HostResourceAccessKind.Allow);

        // Initialize IPC bridge dispatcher
        var services = DesktopServiceContainer.Services;
        _dispatcher = new DesktopIpcDispatcher(
            services,
            services.GetRequiredService<SchemaIntrospectorFactory>(),
            services.GetRequiredService<DabConfigService>(),
            services.GetRequiredService<DabProcessManager>(),
            services.GetRequiredService<ILlmSettingsStore>(),
            services.GetRequiredService<ISavedConnectionStore>(),
            services.GetRequiredService<IMutationService>(),
            services.GetRequiredService<IWriteAuditStore>(),
            services.GetRequiredService<IConnectionStringProtector>(),
            services.GetRequiredService<ILogger<DesktopIpcDispatcher>>(),
            json =>
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        AppWebView.CoreWebView2?.PostWebMessageAsJson(json);
                    }
                    catch
                    {
                        // Handle window close race condition
                    }
                });
            });

        // Wire navigation completed to smoothly dismiss loading overlay
        AppWebView.NavigationCompleted += (s, e) =>
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                // Brief 150ms buffer to allow the rendered DOM frame to paint
                await Task.Delay(150);
                HideLoadingOverlay();
            });
        };

        // Wire incoming message events from JavaScript
        AppWebView.CoreWebView2.WebMessageReceived += async (_, args) =>
        {
            string? raw = null;
            try
            {
                // JavaScript calls postMessage(object), so WebMessageAsJson is the primary source
                raw = args.WebMessageAsJson;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not read WebMessageAsJson: {ex.Message}");
            }

            if (string.IsNullOrEmpty(raw))
            {
                try
                {
                    raw = args.TryGetWebMessageAsString();
                }
                catch
                {
                    // Fall back if message is not a string
                }
            }

            if (!string.IsNullOrEmpty(raw))
            {
                // Fast path for frontend signal that React hydration is complete
                if (raw.Contains("\"app:ready\""))
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        HideLoadingOverlay();
                    });
                    return;
                }

                if (_dispatcher != null)
                {
                    try
                    {
                        await _dispatcher.DispatchMessageAsync(raw);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error dispatching IPC message: {ex.Message}");
                    }
                }
            }
        };

        // Safety fallback: ensure loading overlay never stays visible past 5 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            this.DispatcherQueue.TryEnqueue(() =>
            {
                HideLoadingOverlay();
            });
        });

        // Navigate to the local virtual host
        AppWebView.Source = new Uri("https://zeroquery.local/index.html");
    }

    private void HideLoadingOverlay()
    {
        if (LoadingOverlay.Visibility != Visibility.Collapsed)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            StartupProgressRing.IsActive = false;
        }
    }
}
