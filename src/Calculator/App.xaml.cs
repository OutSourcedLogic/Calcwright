// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

//
// App.xaml.h
// Declaration of the App class.
//

using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.StartScreen;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

using CalculatorApp.Utils;
using CalculatorApp.ViewModel.Common;
using CalculatorApp.ViewModel.Common.Automation;

namespace CalculatorApp
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public sealed partial class App
    {
        /// <summary>
        /// Initializes the singleton application object. This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            NarratorNotifier.RegisterDependencyProperties();

            // TODO: MSFT 14645325: Set this directly from XAML.
            // Currently this is bugged so the property is only respected from code-behind.
            HighContrastAdjustment = ApplicationHighContrastAdjustment.None;
            Suspending += OnSuspending;
#if DEBUG
            DebugSettings.IsBindingTracingEnabled = true;
            DebugSettings.BindingFailed += (sender, args) =>
            {
                if (Debugger.IsAttached)
                {
                    _ = args.Message; // Inspect in debugger
                    Debugger.Break();
                }
            };
#endif
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user. Other entry points
        /// will be used when the application is launched to open a specific file, to display
        /// search results, and so forth.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            NavCategoryStates.SetCurrentUser(args.User.NonRoamableId);

            // It takes time to check GraphingMode at the very first time. Warm up in a background thread.
            Task.Run(() => NavCategoryStates.IsViewModeEnabled(ViewMode.Graphing));

            OnAppLaunch(args, args.Arguments, args.PrelaunchActivated);
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            if (args.Kind != ActivationKind.Protocol)
            {
                return;
            }
            else if (args.TryGetSnapshotProtocol(out var protoArgs))
            {
                OnAppLaunch(args, protoArgs.GetSnapshotLaunchArgs(), false);
            }
            else
            {
                // handle any unknown protocol launch as a normal app launch.
                OnAppLaunch(args, null, false);
            }
        }

        private void OnAppLaunch(IActivatedEventArgs args, object arguments, bool isPreLaunch)
        {
            // Uncomment the following lines to display frame-rate and per-frame CPU usage info.
            //#if DEBUG
            //    if (IsDebuggerPresent())
            //    {
            //        DebugSettings.EnableFrameRateCounter = true;
            //    }
            //#endif

            args.SplashScreen.Dismissed += async (_, __) => await SetupJumpListAsync();

            var minWindowWidth = Convert.ToSingle(Resources["AppMinWindowWidth"]);
            var minWindowHeight = Convert.ToSingle(Resources["AppMinWindowHeight"]);
            var minWindowSize = SizeHelper.FromDimensions(minWindowWidth, minWindowHeight);
            var appView = ApplicationView.GetForCurrentView();
            var localSettings = ApplicationData.Current.LocalSettings;

            // SetPreferredMinSize should always be called before Window.Activate
            appView.SetPreferredMinSize(minWindowSize);

            // Until the app has once been shown and closed normally, open at the stock Windows Calculator
            // size (AppDefaultWindowWidth/Height); after that, Windows reuses whatever size the user left it at.
            // The flag is only recorded on a normal close (OnSuspending), so a first launch that never
            // really showed (a background pre-launch, or the app being closed by an installer) doesn't
            // count. Otherwise Windows falls back to its own, much larger, default size from then on.
            var defaultWindowSize = SizeHelper.FromDimensions(
                Math.Max(minWindowWidth, Convert.ToSingle(Resources["AppDefaultWindowWidth"])),
                Math.Max(minWindowHeight, Convert.ToSingle(Resources["AppDefaultWindowHeight"])));
            bool applyDefaultSize = !isPreLaunch && !localSettings.Values.ContainsKey(DefaultWindowSizeAppliedKey);
            if (applyDefaultSize)
            {
                appView.TryResizeView(defaultWindowSize); // the requested size must not be less than the min size.
                m_defaultWindowSizeShown = true;
            }
            else if (!isPreLaunch)
            {
                ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.Auto;
            }

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame
                {
                    FlowDirection = LocalizationSettings.GetInstance().GetFlowDirection()
                };
            }

            if (isPreLaunch)
            {
                return;
            }

            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            if (rootFrame.Content == null && !rootFrame.Navigate(typeof(MainPage), arguments))
            {
                // We couldn't navigate to the main page, kill the app so we have a good
                // stack to debug
                throw new SystemException("6d430286-eb5d-4f8d-95d2-3d1059552968");
            }

            // Place the frame in the current Window
            Window.Current.Content = rootFrame;
            ThemeHelper.InitializeAppTheme();
            Window.Current.Activate();

            if (applyDefaultSize)
            {
                // A resize requested before the window is on screen can be ignored; ask again once it is.
                // (Windows accepts the request above but can still show the window at its own default,
                // e.g. 1200 x 900.) How the requested height maps to the window also varies by launch: on a
                // relaunch the title bar can be counted inside the request, leaving the window ~33 px short.
                // So resize, measure, and correct once so the window always ends up the same size.
                _ = Window.Current.Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                {
                    var view = ApplicationView.GetForCurrentView();
                    view.TryResizeView(defaultWindowSize);
                    await Task.Delay(400);

                    double titleBarHeight = Windows.ApplicationModel.Core.CoreApplication.GetCurrentView().TitleBar.Height;
                    if (titleBarHeight <= 0)
                    {
                        titleBarHeight = 32;
                    }
                    double wantedHeight = defaultWindowSize.Height + titleBarHeight; // window content incl. title bar area
                    double shortBy = wantedHeight - Window.Current.Bounds.Height;
                    if (Math.Abs(shortBy) > 1)
                    {
                        view.TryResizeView(new Windows.Foundation.Size(defaultWindowSize.Width, defaultWindowSize.Height + shortBy));
                    }
                });
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs args)
        {
            TraceLogger.GetInstance().LogButtonUsage();

            if (m_defaultWindowSizeShown)
            {
                ApplicationData.Current.LocalSettings.Values[DefaultWindowSizeAppliedKey] = true;
            }
        }

        // Replaces the upstream "VeryFirstLaunch" flag, which was set before the window had actually been shown.
        private const string DefaultWindowSizeAppliedKey = "DefaultWindowSizeApplied";
        private bool m_defaultWindowSizeShown;

        private async Task SetupJumpListAsync()
        {
            try
            {
                var calculatorOptions = NavCategoryStates.CreateCalculatorCategoryGroup();

                var jumpList = await JumpList.LoadCurrentAsync();
                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
                jumpList.Items.Clear();

                foreach (NavCategory option in calculatorOptions.Categories)
                {
                    if (!NavCategoryStates.IsViewModeEnabled(option.ViewMode))
                    {
                        continue;
                    }
                    ViewMode mode = option.ViewMode;
                    var item = JumpListItem.CreateWithArguments(((int)mode).ToString(), "ms-resource:///Resources/" + NavCategoryStates.GetNameResourceKey(mode));
                    item.Description = "ms-resource:///Resources/" + NavCategoryStates.GetNameResourceKey(mode);
                    item.Logo = new Uri("ms-appx:///Assets/" + mode + ".png");
                    jumpList.Items.Add(item);
                }

                await jumpList.SaveAsync();
            }
            catch (Exception ex)
            {
                TraceLogger.GetInstance().LogError(ViewMode.None, nameof(SetupJumpListAsync), ex.ToString());
#if DEBUG
                throw;
#endif
            }
        }
    }
}

