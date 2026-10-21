using System;
using System.Collections.Generic;

namespace UhcPvp.Tests;

/// <summary>
/// Base class for tests. Every public parameterless method whose name starts with "Test" runs
/// on a fresh instance. A test fails if a check fails or it throws.
/// </summary>
public abstract class TestCase
{
    public List<string> Failures { get; } = new();

    protected void Check(bool condition, string message)
    {
        if (!condition)
        {
            Failures.Add(message);
        }
    }

    protected void CheckEqual<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            Failures.Add($"{message}: expected {expected}, got {actual}");
        }
    }

    protected void CheckNear(double actual, double expected, double tolerance, string message)
    {
        if (!(Math.Abs(actual - expected) <= tolerance))
        {
            Failures.Add($"{message}: expected {expected} (+/- {tolerance}), got {actual}");
        }
    }
}
