using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Halide.Tests;

public partial class TestRunnerHost : Node
{
	public override async void _Ready()
	{
		try
		{
			string[] args = OS.GetCmdlineUserArgs();
			string prefix = "--test-assembly=";
			string assemblyPath = args.FirstOrDefault(a => a.StartsWith(prefix))?[prefix.Length..];
			if (string.IsNullOrWhiteSpace(assemblyPath)) throw new ArgumentException("Missing --test-assembly=<path>");

			AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(typeof(TestRunnerHost).Assembly);
			Assembly tests = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
			Type runnerType = tests.GetType("Halide.Tests.TestRunner", throwOnError: true);
			object runner = Activator.CreateInstance(runnerType, this);
			await (Task)runnerType.GetMethod("Execute").Invoke(runner, [args]);
		}
		catch (Exception e)
		{
			GD.PushError($"Could not start the separate Halide test assembly: {e}");
			GetTree().Quit(1);
		}
	}
}
