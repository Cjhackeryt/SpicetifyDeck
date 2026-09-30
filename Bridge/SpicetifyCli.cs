using System.Diagnostics;
using System.Text;
using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed record CliResult(bool Succeeded, string Output, int ExitCode, bool TimedOut = false)
{
		public string Summary
	{
		get
		{
			if (TimedOut)
			{
				return "The command did not finish in time.";
			}

			if (Succeeded)
			{
				return string.Empty;
			}

			var lines = Output
				.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(line => !line.StartsWith("spicetify v", StringComparison.OrdinalIgnoreCase))
				.Select(line => line.Trim())
				.Where(line => line.Length > 0)
				.Take(3);

			var text = string.Join(" ", lines);
			return string.IsNullOrEmpty(text) ? $"exit code {ExitCode}" : text;
		}
	}
}

public sealed class SpicetifyCli(ILogger logger)
{
		private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

		private const int MaxCapturedCharacters = 16 * 1024;

	private readonly ILogger _logger = logger.ForContext<SpicetifyCli>();

		public async Task<string?> GetVersionAsync(SpicetifyLocation location, CancellationToken cancellationToken)
	{
		var result = await RunAsync(location, ["-v"], cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			return null;
		}

		return result.Output
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(line => line.Trim())
			.FirstOrDefault(line => line.Length > 0 && char.IsDigit(line[0]) && line.Contains('.'));
	}

		public async Task<IReadOnlyList<string>> GetConfiguredExtensionsAsync(SpicetifyLocation location, CancellationToken cancellationToken)
	{
		var result = await RunAsync(location, ["config", "extensions"], cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			return [];
		}

		return SpicetifyConfig.ParseList(result.Output);
	}

		public Task<CliResult> WriteConfiguredExtensionsAsync(
		SpicetifyLocation location,
		IReadOnlyList<string> extensions,
		CancellationToken cancellationToken) =>

		RunAsync(location, ["config", "extensions", string.Join('|', extensions)], cancellationToken);

		public Task<CliResult> ApplyAsync(SpicetifyLocation location, CancellationToken cancellationToken) =>
		RunAsync(location, ["apply"], cancellationToken);

		public Task<CliResult> AddExtensionAsync(
		SpicetifyLocation location,
		string fileName,
		CancellationToken cancellationToken) =>
		RunAsync(location, ["config", "extensions", fileName], cancellationToken);

		public async Task<CliResult> RemoveExtensionAsync(
		SpicetifyLocation location,
		string fileName,
		CancellationToken cancellationToken)
	{
		if (!SpicetifyConfig.RemoveExtension(location.Root, fileName))
		{
			return new CliResult(
				false,
				$"The Spicetify configuration at {location.ConfigPath} could not be updated.",
				-1);
		}

		_logger.Debug("Removed {FileName} from the Spicetify extension list.", fileName);
		return new CliResult(true, string.Empty, 0);
	}

		public async Task<CliResult> RunAsync(
		SpicetifyLocation location,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken)
	{
		if (location.ExecutablePath is null)
		{
			return new CliResult(false, "The Spicetify CLI was not found.", -1);
		}

		var startInfo = new ProcessStartInfo
		{
			FileName = location.ExecutablePath,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true,

			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};

		if (OperatingSystem.IsWindows()
			&& (location.ExecutablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
				|| location.ExecutablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
		{
			startInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
			startInfo.ArgumentList.Add("/c");
			startInfo.ArgumentList.Add(location.ExecutablePath);
		}

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var process = new Process { StartInfo = startInfo };

		process.Start();

		process.StandardInput.Close();

		var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(Timeout);

		var timedOut = false;
		try
		{
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{

			timedOut = !cancellationToken.IsCancellationRequested;
			TryKill(process);
		}

		var output = Join(standardOutput, standardError);
		var exitCode = SafeExitCode(process);
		var result = new CliResult(!timedOut && exitCode == 0, output, exitCode, timedOut);

		_logger.Debug("spicetify {Arguments} exited {ExitCode}.", string.Join(' ', arguments), exitCode);
		return result;

		static string Join(Task<string> output, Task<string> error)
		{
			var builder = new StringBuilder();
			foreach (var task in new[] { output, error })
			{
				if (!task.IsCompletedSuccessfully)
				{
					continue;
				}

				builder.AppendLine(task.Result);
			}

			var text = builder.ToString().Trim();
			return text.Length > MaxCapturedCharacters ? text[..MaxCapturedCharacters] : text;
		}
	}

	private static int SafeExitCode(Process process)
	{
		try
		{
			return process.HasExited ? process.ExitCode : -1;
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			return -1;
		}
	}

	private static void TryKill(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
		{

		}
	}
}
