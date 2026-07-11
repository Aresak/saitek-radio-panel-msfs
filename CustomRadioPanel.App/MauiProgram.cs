using CustomRadioPanel.App.Services;
using CustomRadioPanel.Core;
using CustomRadioPanel.Core.Logic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace CustomRadioPanel.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddRadioPanel();
		builder.Services.AddSingleton<IWindowControl, WindowControl>();

		// Borderless, fixed-size, centered window.
		builder.ConfigureLifecycleEvents(events =>
			events.AddWindows(w => w.OnWindowCreated(win => WindowControl.ConfigureAtStartup(win, 900, 440))));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Start the panel<->sim bridge immediately so it runs regardless of which UI page is open.
		app.Services.GetRequiredService<RadioPanelController>().Start();

		return app;
	}
}
