using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class WidgetApiProbeTests
{
	[Fact]
	public void DumpArtworkWidgetApi()
	{
		var types = new[]
		{
			typeof(IIntegrationContext), typeof(IUiResourceRegistry), typeof(IUiProvider), typeof(IUiSession),
			typeof(UiImage), typeof(UiButton), typeof(UiStack), typeof(UiTextRun), typeof(UiSurface), typeof(UiResource),
				typeof(UiSize), typeof(UiValue<>), typeof(UiComponentDirections), typeof(UiStringInput),
				typeof(UiChoiceInput), typeof(UiOption), typeof(IVariableApi), typeof(IUserVariableApi),
		};
		var output = string.Join("\n", types.Select(type =>
			$"{type.FullName}: props=[{string.Join(",", type.GetProperties().Select(property => property.Name + ":" + property.PropertyType.Name))}] methods=[{string.Join(";", type.GetMethods().Select(method => method.ToString()))}]"));
		Assert.Fail(output);
	}
}
