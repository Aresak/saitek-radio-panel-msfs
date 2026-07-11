using CustomRadioPanel.Core.Config;
using CustomRadioPanel.Core.Hardware;
using CustomRadioPanel.Core.Logic;
using CustomRadioPanel.Core.Sim;
using Microsoft.Extensions.DependencyInjection;

namespace CustomRadioPanel.Core;

/// <summary>Wires up all Core services.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddRadioPanel(this IServiceCollection services)
    {
        services.AddSingleton<ConfigService>();
        services.AddSingleton<RadioPanelDevice>();
        services.AddSingleton<IRadioPanel>(sp => sp.GetRequiredService<RadioPanelDevice>());

        // SimConnect is talked to via P/Invoke; the native SimConnect.dll is located at runtime
        // (SimConnectLocator). If it isn't present the service simply never connects — the HID half
        // keeps working — so it is always safe to register.
        services.AddSingleton<ISimConnectService, SimConnectService>();

        services.AddSingleton<RadioPanelController>();
        return services;
    }
}
