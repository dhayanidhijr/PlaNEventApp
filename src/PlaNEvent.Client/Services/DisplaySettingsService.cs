using Microsoft.JSInterop;

namespace PlaNEvent.Client.Services;

public sealed class DisplaySettingsService
{
    private const string DefaultTheme = "color";
    private const int DefaultScalePercent = 100;

    private readonly IJSRuntime jsRuntime;
    private bool initialized;

    public DisplaySettingsService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    public string Theme { get; private set; } = DefaultTheme;
    public int ScalePercent { get; private set; } = DefaultScalePercent;

    public event Action? Changed;

    public async Task EnsureInitializedAsync()
    {
        if (initialized)
        {
            return;
        }

        try
        {
            var settings = await jsRuntime.InvokeAsync<DisplaySettingsDto>("planeventDisplay.getSettings");
            Theme = NormalizeTheme(settings.Theme);
            ScalePercent = NormalizeScale(settings.ScalePercent);
        }
        catch
        {
            Theme = DefaultTheme;
            ScalePercent = DefaultScalePercent;
        }

        initialized = true;
        await ApplyAsync();
        Changed?.Invoke();
    }

    public async Task SetThemeAsync(string theme)
    {
        Theme = NormalizeTheme(theme);
        await ApplyAsync();
        Changed?.Invoke();
    }

    public async Task SetScalePercentAsync(int scalePercent)
    {
        ScalePercent = NormalizeScale(scalePercent);
        await ApplyAsync();
        Changed?.Invoke();
    }

    private async Task ApplyAsync()
    {
        await jsRuntime.InvokeVoidAsync("planeventDisplay.applySettings", Theme, ScalePercent);
    }

    private static string NormalizeTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" => "light",
        "dark" => "dark",
        "grayscale" => "grayscale",
        "color" => "color",
        _ => DefaultTheme
    };

    private static int NormalizeScale(int scalePercent)
    {
        if (scalePercent < 50)
        {
            return 50;
        }

        if (scalePercent > 500)
        {
            return 500;
        }

        return scalePercent;
    }

    public sealed class DisplaySettingsDto
    {
        public string? Theme { get; set; }
        public int ScalePercent { get; set; } = DefaultScalePercent;
    }
}
