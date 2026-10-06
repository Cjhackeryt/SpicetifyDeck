using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SpicetifyDeck;
using SpicetifyDeck.Artwork;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using SpicetifyDeck.Ui;
using SpicetifyDeck.Variables;

var builder = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog);

builder.Services.AddHttpClient(nameof(ArtworkFetcher), client =>
{
	client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddSingleton<PluginSettings>();
builder.Services.AddSingleton<SpicetifyLocator>();
builder.Services.AddSingleton<SpicetifyCli>();
builder.Services.AddSingleton<BridgeCredentials>(provider =>

	BridgeCredentials.Load(provider.GetRequiredService<PluginSettings>().DataDirectory, Log.Logger));
builder.Services.AddSingleton<BridgeEndpoint>();
builder.Services.AddSingleton<BridgeScriptGenerator>();
builder.Services.AddSingleton<BridgeConnectionManager>();
builder.Services.AddSingleton<SpicetifyBridgeService>();
builder.Services.AddSingleton<ArtworkFetcher>();
builder.Services.AddSingleton<SpotifyStateManager>();
builder.Services.AddSingleton<SpicetifyDeckUiProvider>();
builder.Services.AddSingleton<SpicetifyVariableProvider>();

builder.Services.AddSingleton<BridgeListener>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BridgeListener>());

var plugin = builder
	.RegisterIntegration<SpicetifyIntegration>()
	.Build();

await plugin.RunAsync();
