using EditSharpGUI.Scripts.App.Platform;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// runs every [TestFixture]'s [Test]s, each fixture in a user-data folder of its own, and quits: 0 when all passed.
//   -- [--filter=TEXT] [--fixture=NAME] [--windowed-only] [--list] [--junit=FILE.xml] [--artifacts=DIR]
// under --headless, [Windowed] tests are skipped; a windowed run with --windowed-only runs just those
public partial class TestRunner : Node
{
	readonly List<TestResult> results = [];
	string artifacts;
	string recordingScratch;

	public override async void _Ready()
	{
		string[] args = OS.GetCmdlineUserArgs();
		string Arg(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[$"--{name}=".Length..];
		bool windowedOnly = args.Contains("--windowed-only");
		string filter = Arg("filter");
		string only = Arg("fixture");
		artifacts = Arg("artifacts");
		if (artifacts is not null) Directory.CreateDirectory(artifacts);

		GetTree().Root.GuiEmbedSubwindows = false;
		HostWindow.Hide(GetTree().Root);
		EditSharp.Compositing.Gpu.GpuDiagnostics.TrackCreation = args.Contains("--gpu-origins");
		string root = Path.Combine(Path.GetTempPath(), $"editsharp-tests-{Guid.NewGuid():N}");

		List<Type> fixtures = [.. typeof(TestRunner).Assembly.GetTypes()
			.Where(t => t.IsDefined(typeof(TestFixtureAttribute)) && !t.IsAbstract && (only is null || t.Name == only))
			.OrderBy(t => t.Name)];

		if (args.Contains("--list"))
		{
			foreach (Type f in fixtures)
				foreach (MethodInfo m in Tests(f)) GD.Print($"TEST {f.Name}.{m.Name}{(Known(f, m) is null ? "" : " [known]")}");
			GetTree().Quit();
			return;
		}

		Stopwatch all = Stopwatch.StartNew();
		foreach (Type fixture in fixtures)
		{
			List<MethodInfo> tests = [.. Tests(fixture).Where(m => filter is null || $"{fixture.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))];
			if (tests.Count == 0) continue;

			TestApp.Sandbox = Path.Combine(root, fixture.Name);
			UserData.Redirect(Path.Combine(TestApp.Sandbox, "user-data"));
			AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
			await TestApp.ResetAsync();

			object instance = Activator.CreateInstance(fixture);
			foreach (MethodInfo test in tests) await Run(fixture, test, instance, windowedOnly);
		}

		Report(all.Elapsed.TotalSeconds, Arg("junit"));
		await TestApp.ResetAsync();
		try { Directory.Delete(root, true); } catch (Exception) { }
		if (recordingScratch is not null) try { Directory.Delete(recordingScratch, true); } catch (Exception) { }

		int code = results.Any(r => r.Outcome == TestOutcome.Failed) ? 1 : 0;
		GD.Print($"TEST RUN EXIT {code}");

		await ProjectManager.Singleton.QuitAsync(code);
	}

	static string Known(Type fixture, MethodInfo test) =>
		(test.GetCustomAttribute<KnownIssueAttribute>() ?? fixture.GetCustomAttribute<KnownIssueAttribute>())?.Issue;

	static IEnumerable<MethodInfo> Tests(Type fixture) => fixture.GetMethods(BindingFlags.Instance | BindingFlags.Public)
		.Where(m => m.IsDefined(typeof(TestAttribute)))
		.OrderBy(m => m.MetadataToken);

	async Task Run(Type fixture, MethodInfo test, object instance, bool windowedOnly)
	{
		bool windowed = test.IsDefined(typeof(WindowedAttribute)) || fixture.IsDefined(typeof(WindowedAttribute));
		OnlyOnAttribute only = test.GetCustomAttribute<OnlyOnAttribute>() ?? fixture.GetCustomAttribute<OnlyOnAttribute>();

		string skip = windowed && TestApp.Headless ? "needs windows"
			: windowedOnly && !windowed ? "not a windowed test"
			: only is not null && only.OS != OS.GetName() && !(only.OS == "Linux" && OS.GetName() is "Linux" or "FreeBSD") ? $"only on {only.OS}"
			: null;

		if (skip is not null)
		{
			Record(new TestResult(fixture.Name, test.Name, TestOutcome.Skipped, 0, skip));
			return;
		}

		double timeout = test.GetCustomAttribute<TestAttribute>().Timeout;
		Stopwatch watch = Stopwatch.StartNew();
		Watch($"running {fixture.Name}.{test.Name}", timeout + 60);
		TestResult result;

		TestRecorder recorder = artifacts is null || TestApp.Headless ? null : TestRecorder.Start(GetTree(), recordingScratch ??= Path.Combine(Path.GetTempPath(), $"editsharp-recordings-{Guid.NewGuid():N}"));

		try
		{
			await Invoke(fixture, instance, typeof(SetUpAttribute));
			Task run = Call(test, instance);
			Task done = await Task.WhenAny(run, TestApp.Seconds(timeout));
			if (done != run) throw new AssertionException($"didn't finish within {timeout} seconds");
			await run;
			result = new TestResult(fixture.Name, test.Name, TestOutcome.Passed, watch.Elapsed.TotalSeconds);
		}
		catch (Exception e)
		{
			Exception cause = e is TargetInvocationException { InnerException: Exception inner } ? inner : e;
			result = cause is SkipException
				? new TestResult(fixture.Name, test.Name, TestOutcome.Skipped, watch.Elapsed.TotalSeconds, cause.Message)
				: new TestResult(fixture.Name, test.Name, TestOutcome.Failed, watch.Elapsed.TotalSeconds,
					cause is AssertionException ? cause.Message : $"{cause.GetType().Name}: {cause.Message}\n{cause.StackTrace}");
		}

		// only a failure gets a recording -- the last several seconds of what was on screen, not a
		// screenshot of every test whatever the outcome
		if (recorder is not null) await recorder.FinishAsync(result.Outcome == TestOutcome.Failed, artifacts, $"{fixture.Name}.{test.Name}");

		// a known bug still failing is reported, not counted against the run
		if (result.Outcome == TestOutcome.Failed && Known(fixture, test) is string issue)
			result = result with { Outcome = TestOutcome.Known, Message = $"known issue ({issue}): {result.Message}" };

		try { await Invoke(fixture, instance, typeof(TearDownAttribute)); }
		catch (Exception e) { GD.PrintErr($"tear-down of {fixture.Name}.{test.Name} failed: {e.Message}"); }

		Watch($"resetting after {fixture.Name}.{test.Name}", 90);
		await TestApp.ResetAsync();
		Watch(null, 0);
		Record(result);
		GD.Print($"  gpu contexts alive: {EditSharp.Compositing.Gpu.GpuDiagnostics.LiveContexts}");
		if (OS.GetCmdlineUserArgs().Contains("--gpu-origins"))
			foreach (string origin in EditSharp.Compositing.Gpu.GpuDiagnostics.LiveOrigins)
				GD.Print("  alive from:\n" + string.Join("\n", origin.Split('\n').Where(l => l.Contains("EditSharp")).Take(12)));
	}

	static string watched;
	static DateTime watchedUntil;
	static System.Threading.Thread watchdog;

	// a main thread blocked past its limit can't time the test out itself, so another thread reports it and exits
	static void Watch(string phase, double seconds)
	{
		lock (typeof(TestRunner)) { watched = phase; watchedUntil = DateTime.UtcNow.AddSeconds(seconds); }
		if (watchdog is not null) return;
		watchdog = new System.Threading.Thread(() =>
		{
			while (true)
			{
				System.Threading.Thread.Sleep(2000);
				lock (typeof(TestRunner))
				{
					if (watched is null || DateTime.UtcNow < watchedUntil) continue;
					System.Console.WriteLine($"HUNG {watched}: the main thread stopped responding");
					System.Console.Out.Flush();
					PrintStacks();
					System.Environment.Exit(3);
				}
			}
		}) { IsBackground = true, Name = "TestWatchdog" };
		watchdog.Start();
	}

	// every thread's managed stack, when ES_HANG_STACKS names a dotnet-stack executable (CI sets it)
	static void PrintStacks()
	{
		string tool = System.Environment.GetEnvironmentVariable("ES_HANG_STACKS");
		if (string.IsNullOrEmpty(tool)) return;
		if (!File.Exists(tool) && File.Exists(tool + ".exe")) tool += ".exe";
		try
		{
			using Process stack = Process.Start(new ProcessStartInfo(tool, $"report -p {System.Environment.ProcessId}") { RedirectStandardOutput = true, RedirectStandardError = true });
			string output = stack.StandardOutput.ReadToEnd() + stack.StandardError.ReadToEnd();
			stack.WaitForExit(60000);
			System.Console.WriteLine(output);
			System.Console.Out.Flush();
		}
		catch (Exception e) { System.Console.WriteLine($"dotnet-stack failed: {e.Message}"); }
	}

	static async Task Invoke(Type fixture, object instance, Type attribute)
	{
		foreach (MethodInfo m in fixture.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => m.IsDefined(attribute)))
			await Call(m, instance);
	}

	static Task Call(MethodInfo method, object instance) => method.Invoke(instance, null) as Task ?? Task.CompletedTask;

	void Record(TestResult result)
	{
		results.Add(result);
		string mark = result.Outcome switch { TestOutcome.Passed => "PASS", TestOutcome.Failed => "FAIL", TestOutcome.Known => "KNOWN", _ => "SKIP" };
		GD.Print($"{mark} {result.Fixture}.{result.Test} ({result.Seconds:0.00}s){(result.Message is null ? "" : $" - {result.Message}")}");
	}

	void Report(double seconds, string junit)
	{
		int passed = results.Count(r => r.Outcome == TestOutcome.Passed);
		int failed = results.Count(r => r.Outcome == TestOutcome.Failed);
		int skipped = results.Count(r => r.Outcome == TestOutcome.Skipped);
		int known = results.Count(r => r.Outcome == TestOutcome.Known);
		GD.Print($"TESTS {passed} passed, {failed} failed, {skipped} skipped, {known} known issues in {seconds:0.0}s");

		foreach (TestResult r in results.Where(r => r.Outcome == TestOutcome.Failed)) GD.Print($"FAILED {r.Fixture}.{r.Test}: {r.Message}");

		if (junit is null) return;

		StringBuilder xml = new();
		xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
		xml.AppendLine($"<testsuites tests=\"{results.Count}\" failures=\"{failed}\" skipped=\"{skipped + known}\" time=\"{seconds:0.000}\">");
		foreach (IGrouping<string, TestResult> suite in results.GroupBy(r => r.Fixture))
		{
			xml.AppendLine($"  <testsuite name=\"{Escape(suite.Key)}\" tests=\"{suite.Count()}\" failures=\"{suite.Count(r => r.Outcome == TestOutcome.Failed)}\" skipped=\"{suite.Count(r => r.Outcome is TestOutcome.Skipped or TestOutcome.Known)}\">");
			foreach (TestResult r in suite)
			{
				xml.Append($"    <testcase classname=\"{Escape(r.Fixture)}\" name=\"{Escape(r.Test)}\" time=\"{r.Seconds:0.000}\"");
				if (r.Outcome == TestOutcome.Passed) xml.AppendLine(" />");
				else if (r.Outcome is TestOutcome.Skipped or TestOutcome.Known) xml.AppendLine($"><skipped message=\"{Escape(r.Message)}\" /></testcase>");
				else xml.AppendLine($"><failure message=\"{Escape(r.Message?.Split('\n')[0])}\">{Escape(r.Message)}</failure></testcase>");
			}
			xml.AppendLine("  </testsuite>");
		}
		xml.AppendLine("</testsuites>");

		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(junit))!);
		File.WriteAllText(junit, xml.ToString());
	}

	static string Escape(string text) => System.Security.SecurityElement.Escape(text ?? "");
}
