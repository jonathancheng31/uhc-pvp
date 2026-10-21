using System;
using System.Linq;
using System.Reflection;
using Godot;

namespace UhcPvp.Tests;

/// <summary>
/// Finds every <see cref="TestCase"/> subclass, runs its Test* methods, prints the results and
/// exits with code 1 if anything failed. Run it with tools/run_tests.sh. An optional
/// "--filter=TEXT" after "--" runs only tests whose class or method name contains TEXT.
/// </summary>
public partial class TestRunner : Node
{
    public override void _Ready()
    {
        string filter = OS.GetCmdlineUserArgs()
            .Where(a => a.StartsWith("--filter=", StringComparison.Ordinal))
            .Select(a => a["--filter=".Length..])
            .FirstOrDefault() ?? "";
        int passed = 0;
        int failed = 0;
        var suites = typeof(TestRunner).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TestCase)) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        foreach (Type suite in suites)
        {
            var tests = suite.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("Test", StringComparison.Ordinal) && m.GetParameters().Length == 0)
                .OrderBy(m => m.MetadataToken);
            foreach (MethodInfo test in tests)
            {
                string name = $"{suite.Name}.{test.Name}";
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var instance = (TestCase)Activator.CreateInstance(suite)!;
                try
                {
                    test.Invoke(instance, null);
                }
                catch (TargetInvocationException e)
                {
                    instance.Failures.Add($"threw {e.InnerException}");
                }
                if (instance.Failures.Count == 0)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    foreach (string failure in instance.Failures)
                    {
                        GD.Print($"FAIL {name}: {failure}");
                    }
                }
            }
        }
        GD.Print($"{passed} passed, {failed} failed");
        GetTree().Quit(failed > 0 || passed == 0 ? 1 : 0);
    }
}
