using System.Collections.Generic;
using CalculatorDemo.Coverable;

namespace CalculatorDemo.Tests;

public sealed class FakeIo : ICalculatorIo
{
    public string Display = "";
    public string Memory = "";
    public List<string> Paper = new();
    public List<(string title, string message)> Errors = new();

    public void SetDisplay(string text) => Display = text;
    public void SetMemoryLabel(string text) => Memory = text;
    public void AppendPaper(string arguments, string result) => Paper.Add(arguments + " = " + result);
    public void ClearPaper() => Paper.Clear();
    public void ShowError(string title, string message) => Errors.Add((title, message));
}
