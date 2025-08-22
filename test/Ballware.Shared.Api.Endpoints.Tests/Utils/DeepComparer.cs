using System.Reflection;

namespace Ballware.Shared.Api.Endpoints.Tests.Utils;

public static class DeepComparer
{
    public static bool AreEqual<T>(T expected, T actual, Action<string>? reportDifference = null)
    {
        return AreObjectsEqual(expected, actual, reportDifference, "");
    }

    public static bool AreListsEqual<T>(IEnumerable<T>? expectedList, IEnumerable<T>? actualList, Action<string>? reportDifference = null)
    {
        if (expectedList == null || actualList == null)
        {
            if (expectedList == null && actualList == null)
                return true;

            reportDifference?.Invoke("One list is null while the other is not.");
            return false;
        }

        var expectedArray = expectedList.ToArray();
        var actualArray = actualList.ToArray();

        if (expectedArray.Length != actualArray.Length)
        {
            reportDifference?.Invoke($"List length mismatch: expected {expectedArray.Length}, actual {actualArray.Length}");
            return false;
        }

        bool allEqual = true;
        for (int i = 0; i < expectedArray.Length; i++)
        {
            if (!AreObjectsEqual(expectedArray[i], actualArray[i], reportDifference, $"[#{i}]"))
            {
                allEqual = false;
            }
        }

        return allEqual;
    }

    private static bool AreObjectsEqual(object? expected, object? actual, Action<string>? reportDifference, string path)
    {
        if (ReferenceEquals(expected, actual)) return true;
        if (expected == null || actual == null)
        {
            reportDifference?.Invoke($"{path}: Expected {(expected ?? "null")}, but got {(actual ?? "null")}");
            return false;
        }

        var type = expected.GetType();

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(DateTime))
        {
            if (!Equals(expected, actual))
            {
                reportDifference?.Invoke($"{path}: Expected '{expected}', but got '{actual}'");
                return false;
            }
            return true;
        }

        bool allEqual = true;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var expectedVal = prop.GetValue(expected);
            var actualVal = prop.GetValue(actual);

            string propPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";

            if (!AreObjectsEqual(expectedVal, actualVal, reportDifference, propPath))
            {
                allEqual = false;
            }
        }

        return allEqual;
    }
}